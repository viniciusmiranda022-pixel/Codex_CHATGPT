# DirectoryAnalyzer On-Prem Agent Architecture (Production-Grade)

This document specifies a concrete, production-oriented on-prem agent architecture for DirectoryAnalyzer that enables **secure remote execution of predefined read-only inventory actions** inside customer environments.

---

## 1. Architectural Goal

**Why an agent-based model is used**
* Directory environments are protected by strict inbound rules and segmentation; an on-prem agent allows inventory to be executed **locally** where directory permissions and network adjacency already exist, while the Analyzer can remain in a separate management network.  
* Agents **reduce credential exposure**: the Analyzer never needs domain credentials to run inventory; instead, the agent runs under a controlled service account with read-only access.  
* The agent gives **deterministic control** over which actions are allowed, preventing arbitrary execution.  

**Security problems solved vs direct remote PowerShell**
* Eliminates open WinRM/PowerShell remoting endpoints and dynamic runspaces across the network.  
* Removes exposure of administrator credentials to remote endpoints (no Just-In-Time delegation required on analyzer).  
* Allows strict allow-listing of operations (ActionName → method) and enforced read-only behavior.  

**How the architecture minimizes attack surface**
* Single hardened HTTPS endpoint with mTLS.  
* No remote command execution: **only predefined actions**.  
* Tight TLS policy (TLS 1.2+), certificate pinning/allow-list, and service running as least-privileged account.  

**Component list**
* **Analyzer**: Central DirectoryAnalyzer application that calls agents.  
* **Agent (Windows Service)**: Local inventory service exposing HTTPS API.  
* **Active Directory (AD)**: Directory to query (read-only).  
* **Certificates**: Mutual TLS (agent + analyzer).  
* **Network**: Single inbound port to agent; strict firewall and segment rules.  

---

## 2. Agent Runtime Model

**Windows Service base class example**
```csharp
using System;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace DirectoryAnalyzer.Agent
{
    public sealed class InventoryAgentService : ServiceBase
    {
        private CancellationTokenSource? _cts;
        private AgentHost? _host;

        public InventoryAgentService()
        {
            ServiceName = "DirectoryAnalyzerAgent";
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            _cts = new CancellationTokenSource();
            _host = new AgentHost();
            _host.StartAsync(_cts.Token).GetAwaiter().GetResult();
        }

        protected override void OnStop()
        {
            _cts?.Cancel();
            _host?.StopAsync().GetAwaiter().GetResult();
        }

        public static void Main()
        {
            ServiceBase.Run(new InventoryAgentService());
        }
    }
}
```

**Service start/stop & listener initialization**
* `OnStart`: loads config, initializes HTTPS listener, registers actions, and begins request loop.  
* `OnStop`: stops listener, flushes logs, cancels in-flight requests.  

**Configuration loading (JSON)**
```csharp
using System.IO;
using System.Text.Json;

public sealed class AgentConfig
{
    public string BindPrefix { get; set; } = "https://+:8443/agent/";
    public string CertThumbprint { get; set; } = string.Empty;
    public string[] AnalyzerClientThumbprints { get; set; } = Array.Empty<string>();
    public int ActionTimeoutSeconds { get; set; } = 30;
    public string LogPath { get; set; } = @"C:\ProgramData\DirectoryAnalyzer\agent.log";
}

public static class ConfigLoader
{
    public static AgentConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AgentConfig>(json)
            ?? throw new InvalidOperationException("Invalid agent config.");
    }
}
```

**Service account**
* Runs as a **domain service account** or **gMSA** with **read-only AD permissions**.  
* Avoids LocalSystem to reduce access scope and lateral movement risks.  
* Deny interactive login; allow “Log on as a service.”  

---

## 3. Secure Communication Channel (Schannel)

**HTTPS listener code**
```csharp
using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public sealed class AgentHost
{
    private readonly HttpListener _listener = new HttpListener();
    private AgentConfig? _config;
    private ActionRegistry? _registry;

    public async Task StartAsync(CancellationToken token)
    {
        _config = ConfigLoader.Load(@"C:\ProgramData\DirectoryAnalyzer\agent.json");
        _registry = new ActionRegistry();

        _listener.Prefixes.Add(_config.BindPrefix);
        _listener.Start();

        while (!token.IsCancellationRequested)
        {
            var ctx = await _listener.GetContextAsync().ConfigureAwait(false);
            _ = Task.Run(() => HandleRequestAsync(ctx, token), token);
        }
    }

    public Task StopAsync()
    {
        _listener.Stop();
        return Task.CompletedTask;
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken token)
    {
        ctx.Response.ContentType = "application/json";
        try
        {
            var request = await JsonSerializer.DeserializeAsync<AgentRequest>(
                ctx.Request.InputStream, cancellationToken: token);

            if (request == null)
            {
                ctx.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                await WriteJsonAsync(ctx, new AgentResponse("InvalidRequest"));
                return;
            }

            var result = await _registry!.ExecuteAsync(request, _config!, token);
            ctx.Response.StatusCode = (int)HttpStatusCode.OK;
            await WriteJsonAsync(ctx, result);
        }
        catch (Exception ex)
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteJsonAsync(ctx, AgentResponse.FromException(ex));
        }
    }

    private static Task WriteJsonAsync(HttpListenerContext ctx, AgentResponse response)
    {
        var payload = JsonSerializer.Serialize(response);
        var buffer = Encoding.UTF8.GetBytes(payload);
        return ctx.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
    }
}
```

**Certificate binding (Schannel)**
* Bind using `netsh http add sslcert ...` (see Section 4).  
* The listener uses `HttpListener` over HTTPS with Windows TLS stack.  

**TLS restrictions (TLS 1.2+)**
* Set OS-level policies (group policy or registry) to disable TLS < 1.2 and disable RC4.  
* Example registry (set via GPO in production):
```
HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.0\Server\Enabled=0
HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.1\Server\Enabled=0
HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Server\Enabled=1
```

**Analyzer validates agent certificate**
* The Analyzer uses TLS validation + certificate pinning (thumbprint allow-list).  

**Agent validates analyzer certificate**
* **mTLS** required: only Analyzer client certificates in allow-list are accepted.  
* The agent inspects `HttpListenerRequest.GetClientCertificate()` and validates the thumbprint against `AnalyzerClientThumbprints`.  

```csharp
private bool ValidateClientCertificate(HttpListenerRequest request, AgentConfig config)
{
    var cert = request.GetClientCertificate();
    if (cert == null) return false;
    var thumbprint = cert.GetCertHashString();
    return config.AnalyzerClientThumbprints
        .Any(t => string.Equals(t, thumbprint, StringComparison.OrdinalIgnoreCase));
}
```

---

## 4. Certificate Lifecycle

**Create certificate template (AD CS)**
```powershell
certutil -dstemplate DirectoryAnalyzerAgent
```

**Issue certificate (Agent)**
```powershell
certreq -new agent.inf agent.req
certreq -submit agent.req agent.cer
certreq -accept agent.cer
```

**Export/import certificate**
```powershell
# Export to PFX (Agent)
certutil -f -p "StrongPassword!" -exportpfx My "AGENT_CERT_THUMBPRINT" C:\temp\agent.pfx

# Import on Agent
certutil -f -p "StrongPassword!" -importpfx My C:\temp\agent.pfx
```

**Bind certificate to HTTPS port**
```powershell
netsh http add sslcert ipport=0.0.0.0:8443 `
    certhash=AGENT_CERT_THUMBPRINT `
    appid='{C33B62F9-9B7E-4A9C-BBE5-DAA71B4A6901}'
```

**Self-signed (lab)**
```powershell
$cert = New-SelfSignedCertificate `
    -DnsName "agent01.contoso.local" `
    -CertStoreLocation "cert:\LocalMachine\My" `
    -KeyLength 2048 `
    -NotAfter (Get-Date).AddYears(2)
```

---

## 5. Request/Response Contract

**Request schema**
```json
{
  "RequestId": "string (GUID)",
  "ActionName": "string",
  "Parameters": { "key": "value" }
}
```

**Response schema**
```json
{
  "RequestId": "string (GUID)",
  "Status": "Success | Failed",
  "DurationMs": 123,
  "Payload": { "key": "value" },
  "Error": {
    "Code": "string",
    "Message": "string",
    "Details": "string"
  }
}
```

**Example request**
```json
{
  "RequestId": "0f8fad5b-d9cb-469f-a165-70867728950e",
  "ActionName": "GetUsers",
  "Parameters": { "IncludeDisabled": false }
}
```

**Example response**
```json
{
  "RequestId": "0f8fad5b-d9cb-469f-a165-70867728950e",
  "Status": "Success",
  "DurationMs": 821,
  "Payload": {
    "Users": [
      { "SamAccountName": "jsmith", "Enabled": true }
    ]
  },
  "Error": null
}
```

---

## 6. Action Registry Pattern (No Raw PowerShell)

**Action registry class**
```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed class ActionRegistry
{
    private readonly Dictionary<string, Func<AgentRequest, CancellationToken, Task<AgentResponse>>> _actions;

    public ActionRegistry()
    {
        _actions = new Dictionary<string, Func<AgentRequest, CancellationToken, Task<AgentResponse>>>(
            StringComparer.OrdinalIgnoreCase)
        {
            { "GetUsers", GetUsersAsync }
        };
    }

    public Task<AgentResponse> ExecuteAsync(AgentRequest request, AgentConfig config, CancellationToken token)
    {
        if (!_actions.TryGetValue(request.ActionName, out var action))
        {
            return Task.FromResult(AgentResponse.Failed(request.RequestId, "UnknownAction", "Action not permitted."));
        }
        return action(request, token);
    }

    private static async Task<AgentResponse> GetUsersAsync(AgentRequest request, CancellationToken token)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var includeDisabled = request.Parameters?.GetValueOrDefault("IncludeDisabled")?.GetBoolean() ?? false;

        var users = await AdQueries.GetUsersAsync(includeDisabled, token);
        sw.Stop();

        return AgentResponse.Success(request.RequestId, sw.ElapsedMilliseconds, new { Users = users });
    }
}
```

**Example action implementation (GetUsers)**
```csharp
using System.Collections.Generic;
using System.DirectoryServices.AccountManagement;
using System.Threading;
using System.Threading.Tasks;

public static class AdQueries
{
    public static Task<List<object>> GetUsersAsync(bool includeDisabled, CancellationToken token)
    {
        var results = new List<object>();
        using var context = new PrincipalContext(ContextType.Domain);
        using var searcher = new PrincipalSearcher(new UserPrincipal(context));

        foreach (var result in searcher.FindAll())
        {
            if (token.IsCancellationRequested) break;
            var user = (UserPrincipal)result;
            if (!includeDisabled && user.Enabled == false) continue;

            results.Add(new { user.SamAccountName, Enabled = user.Enabled ?? false });
        }

        return Task.FromResult(results);
    }
}
```

**How new actions are added safely**
* Implement a new method with a **bounded data model** and register it in the dictionary.  
* No direct PowerShell or arbitrary commands are accepted.  
* Code review + unit tests required for new actions.  

---

## 7. Execution Engine

**How actions are executed**
* Each action runs under a per-request timeout.  
* Uses `CancellationToken` to stop long-running queries.  

**Timeout handling**
```csharp
public async Task<AgentResponse> ExecuteAsync(AgentRequest request, AgentConfig config, CancellationToken token)
{
    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
    timeoutCts.CancelAfter(TimeSpan.FromSeconds(config.ActionTimeoutSeconds));

    if (!_actions.TryGetValue(request.ActionName, out var action))
    {
        return AgentResponse.Failed(request.RequestId, "UnknownAction", "Action not permitted.");
    }

    try
    {
        return await action(request, timeoutCts.Token);
    }
    catch (OperationCanceledException)
    {
        return AgentResponse.Failed(request.RequestId, "Timeout", "Action exceeded time limit.");
    }
}
```

**Memory limits**
* Enforced via Windows Job Objects or service-level quotas.  
* Deny exporting huge datasets; return pagination and partial results.  

**Exception handling**
* Catch exceptions, return normalized error payload with code and message.  
* Log stack trace in local logs only (not returned to client).  

**Return normalization**
* Always return consistent `{Status, Payload, Error}` structure.  

---

## 8. Logging & Auditing

**Log format**
* JSON lines (one entry per request) for easy SIEM ingestion.  

**Fields captured**
* `TimestampUtc`, `RequestId`, `ActionName`, `ClientThumbprint`, `DurationMs`, `Status`, `ErrorCode`.  

**Example log entry**
```json
{"TimestampUtc":"2024-10-04T10:45:31Z","RequestId":"0f8fad5b-d9cb-469f-a165-70867728950e","ActionName":"GetUsers","ClientThumbprint":"A1B2C3...","DurationMs":821,"Status":"Success","ErrorCode":null}
```

**Log storage**
* `C:\ProgramData\DirectoryAnalyzer\agent.log` with log rotation (daily + size caps).  

---

## 9. Analyzer Client Integration

**C# client call**
```csharp
using System;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public sealed class AgentClient
{
    private readonly HttpClient _client;

    public AgentClient(X509Certificate2 clientCert, string[] allowedAgentThumbprints)
    {
        var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(clientCert);
        handler.ServerCertificateCustomValidationCallback =
            (_, cert, _, _) => cert != null &&
                allowedAgentThumbprints.Contains(cert.GetCertHashString(), StringComparer.OrdinalIgnoreCase);

        _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<AgentResponse> ExecuteAsync(Uri endpoint, AgentRequest request)
    {
        var json = JsonSerializer.Serialize(request);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync(endpoint, content);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<AgentResponse>(payload)
            ?? throw new InvalidOperationException("Invalid response.");
    }
}
```

**Certificate validation logic**
* Agent certificate thumbprint allow-list on the Analyzer.  
* Analyzer client certificate is presented to agent for mTLS.  

**Error handling**
* HTTP error codes mapped to Analyzer UI; agent response is logged and surfaced in analyzer status panel.  

---

## 10. Network & Firewall

**Required ports**
* Inbound to agent: TCP 8443 (or customer-selected).  
* No outbound required (agent is passively listened to by analyzer).  

**Traffic direction**
* Analyzer → Agent (HTTPS/mTLS).  

**Network flow diagram**
```
[Analyzer] --HTTPS mTLS (8443)--> [Agent Service] --Local LDAP/Kerberos--> [AD]
```

---

## 11. Security Threat Model

**Top attack scenarios & mitigations**
1. **Credential theft**  
   * Mitigation: Analyzer has no AD creds; agent runs under scoped service account or gMSA.  
2. **MITM / TLS downgrade**  
   * Mitigation: TLS 1.2+ only; certificate pinning; mTLS.  
3. **Command injection / arbitrary execution**  
   * Mitigation: Action registry only; no raw PowerShell input accepted.  
4. **Lateral movement**  
   * Mitigation: Least-privileged service account; deny interactive logon; no WinRM exposure.  
5. **Data exfiltration**  
   * Mitigation: Read-only actions only; bounded responses with pagination; logs audit all actions.  

---

## 12. Deployment Steps (On-Prem)

1. **Install agent binaries** to `C:\Program Files\DirectoryAnalyzer\Agent`.  
2. **Create service account or gMSA** with read-only AD permissions.  
3. **Install agent certificate** (LocalMachine\My) and bind HTTPS port.  
4. **Configure agent JSON** at `C:\ProgramData\DirectoryAnalyzer\agent.json`.  
5. **Install Windows Service**:
   ```powershell
   sc.exe create DirectoryAnalyzerAgent binPath= "C:\Program Files\DirectoryAnalyzer\Agent\DirectoryAnalyzer.Agent.exe"
   sc.exe config DirectoryAnalyzerAgent obj= "CONTOSO\svc_diranalyzer" password= "StrongPassword!"
   sc.exe start DirectoryAnalyzerAgent
   ```
6. **Open firewall port** for TCP 8443 inbound from Analyzer subnet only.  
7. **Install Analyzer client certificate** on Analyzer and configure allow-list in Analyzer config.  
8. **Validate** by sending a `GetUsers` request from Analyzer UI.  

---

## Supporting Models

```csharp
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class AgentRequest
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
    public string ActionName { get; set; } = string.Empty;
    public JsonElement? Parameters { get; set; }
}

public sealed class AgentResponse
{
    public string RequestId { get; set; }
    public string Status { get; set; }
    public long DurationMs { get; set; }
    public object? Payload { get; set; }
    public AgentError? Error { get; set; }

    public AgentResponse(string status)
    {
        RequestId = string.Empty;
        Status = status;
    }

    public static AgentResponse Success(string requestId, long durationMs, object payload) =>
        new AgentResponse("Success")
        {
            RequestId = requestId,
            DurationMs = durationMs,
            Payload = payload
        };

    public static AgentResponse Failed(string requestId, string code, string message) =>
        new AgentResponse("Failed")
        {
            RequestId = requestId,
            Error = new AgentError { Code = code, Message = message }
        };

    public static AgentResponse FromException(Exception ex) =>
        new AgentResponse("Failed")
        {
            Error = new AgentError
            {
                Code = "UnhandledException",
                Message = ex.Message,
                Details = ex.StackTrace
            }
        };
}

public sealed class AgentError
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
}
```
