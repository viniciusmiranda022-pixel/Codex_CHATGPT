# DirectoryAnalyzer Agent Prototype (Minimal Working Implementation)

This prototype adds a Windows Service agent and a console Analyzer client with **one action**: `GetUsers`. It uses HTTPS over Schannel with **mutual TLS** (mTLS) and a strict allow-list of certificate thumbprints.

> **Projects added**
> * `AgentService` – Windows Service (DirectoryAnalyzer.Agent)
> * `AnalyzerClient` – Console client (DirectoryAnalyzer.AnalyzerClient)
> * `PrototypeConfigs` – sample JSON configuration files

---

## Build Instructions

### Build in Visual Studio
1. Open `DirectoryAnalyzer.sln`.
2. Build **AgentService** and **AnalyzerClient** projects.

### Build with MSBuild
```powershell
msbuild DirectoryAnalyzer.sln /p:Configuration=Release
```

---

## Certificate Setup (Lab)

> **Goal**: Create an agent TLS certificate (server) and a client certificate (Analyzer). Replace the thumbprints in the config files.

### Agent certificate (server)
```powershell
$agentCert = New-SelfSignedCertificate `
  -DnsName "agent01.contoso.local" `
  -CertStoreLocation "cert:\LocalMachine\My" `
  -KeyLength 2048 `
  -KeyExportPolicy Exportable `
  -NotAfter (Get-Date).AddYears(2)

$agentThumb = $agentCert.Thumbprint
```

 codex/design-production-grade-on-premises-agent-architecture-mn24bx
```powershell
# Trust the self-signed agent certificate for client validation (lab only)
Export-Certificate -Cert $agentCert -FilePath C:\Temp\agent.cer
Import-Certificate -FilePath C:\Temp\agent.cer -CertStoreLocation cert:\LocalMachine\Root
```


 main
### Analyzer client certificate
```powershell
$clientCert = New-SelfSignedCertificate `
  -DnsName "DirectoryAnalyzerClient" `
  -CertStoreLocation "cert:\CurrentUser\My" `
  -KeyLength 2048 `
  -KeyExportPolicy Exportable `
  -NotAfter (Get-Date).AddYears(2)

$clientThumb = $clientCert.Thumbprint
```

 codex/design-production-grade-on-premises-agent-architecture-mn24bx
```powershell
# Trust the self-signed client certificate for mTLS (lab only)
Export-Certificate -Cert $clientCert -FilePath C:\Temp\client.cer
Import-Certificate -FilePath C:\Temp\client.cer -CertStoreLocation cert:\CurrentUser\Root
```


 main
### Bind certificate to HTTPS port with client cert negotiation
```powershell
netsh http add sslcert ipport=0.0.0.0:8443 `
  certhash=$agentThumb `
  appid='{C33B62F9-9B7E-4A9C-BBE5-DAA71B4A6901}' `
  clientcertnegotiation=enable
```

---

## Configure JSON Settings

 codex/design-production-grade-on-premises-agent-architecture-mn24bx
1. Copy `PrototypeConfigs/agentsettings.json` to `C:\ProgramData\DirectoryAnalyzer\agentsettings.json`.
2. Replace:
   * `CertThumbprint` with `$agentThumb`
   * `AnalyzerClientThumbprints` with `$clientThumb`
   * `MaxRequestBytes` if you need higher request limits

3. Copy `PrototypeConfigs/agentclientsettings.json` to `C:\ProgramData\DirectoryAnalyzer\agentclientsettings.json`.

1. Copy `PrototypeConfigs/agentsettings.json` to `AgentService/bin/Release/agentsettings.json`.
2. Replace:
   * `CertThumbprint` with `$agentThumb`
   * `AnalyzerClientThumbprints` with `$clientThumb`

3. Copy `PrototypeConfigs/analyzersettings.json` to `AnalyzerClient/bin/Release/analyzersettings.json`.
 main
4. Replace:
   * `ClientCertThumbprint` with `$clientThumb`
   * `AllowedAgentThumbprints` with `$agentThumb`
   * `AgentEndpoint` with your agent URL

---

## Run the Agent

### Console mode (for lab/testing)
```powershell
DirectoryAnalyzer.Agent.exe
```

### Windows Service
```powershell
sc.exe create DirectoryAnalyzerAgent binPath= "C:\Path\To\DirectoryAnalyzer.Agent.exe"
sc.exe start DirectoryAnalyzerAgent
```

---

## Run the Analyzer Client

```powershell
DirectoryAnalyzer.AnalyzerClient.exe
```

Example output:
```
Request 0f8fad5b-d9cb-469f-a165-70867728950e completed in 821 ms.
jsmith | John Smith | Enabled=True
```

---

## Action: GetUsers

**Request**
```json
{
  "RequestId": "0f8fad5b-d9cb-469f-a165-70867728950e",
  "ActionName": "GetUsers",
  "Parameters": {
    "IncludeDisabled": "false"
  }
}
```

**Response**
```json
{
  "RequestId": "0f8fad5b-d9cb-469f-a165-70867728950e",
  "Status": "Success",
  "DurationMs": 821,
  "Payload": {
    "Users": [
      { "SamAccountName": "jsmith", "DisplayName": "John Smith", "Enabled": true }
    ]
  }
}
```

---

## Notes
* The agent **only** allows `GetUsers` and rejects any other `ActionName`.
* TLS is enforced via OS policy; disable TLS 1.0/1.1 in Schannel for production.
* The agent verifies the Analyzer client certificate by thumbprint allow-list.
* The Analyzer validates the agent certificate thumbprint.
 codex/design-production-grade-on-premises-agent-architecture-mn24bx

---

## WPF Integration (DirectoryAnalyzer)

1. Copy `PrototypeConfigs/agentclientsettings.json` to `C:\ProgramData\DirectoryAnalyzer\agentclientsettings.json`.
2. In the DirectoryAnalyzer UI, open **Agent Inventory**.
3. Click **Executar** to run `GetUsers` through the agent.

---

## MSI Packaging (WiX)

The WiX project is located at `Installer/DirectoryAnalyzer.Agent.wixproj`.
WiX Toolset v3 must be installed and `WixTargetsPath` available in MSBuild.

### Build MSI
```powershell
msbuild Installer\\DirectoryAnalyzer.Agent.wixproj /p:Configuration=Release
```

### Install MSI
```powershell
msiexec /i Installer\\bin\\Release\\DirectoryAnalyzer.Agent.msi /l*v C:\\Temp\\AgentInstall.log
```

After installation, update `C:\\ProgramData\\DirectoryAnalyzer\\agentsettings.json` with your certificate thumbprints and restart the service:
```powershell
sc.exe stop DirectoryAnalyzerAgent
sc.exe start DirectoryAnalyzerAgent
```

For production, reconfigure the service to run under a gMSA or dedicated service account:
```powershell
sc.exe config DirectoryAnalyzerAgent obj= \"CONTOSO\\gmsaDirectoryAnalyzer$\" password= \"\"
```

 main
