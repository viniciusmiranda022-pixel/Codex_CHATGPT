# Deliverable F: Deployment / Ops Runbook

> Scope: On-prem Windows deployment for **DirectoryAnalyzer Agent** with mTLS (server + client certs), HTTPS binding, MSI install, and connectivity validation from the Analyzer host.

## 1) Generate lab self-signed certs (server + client)

> Run on a **lab CA/admin workstation** with PowerShell as Administrator.

```powershell
# Variables
$DnsName = "agent01.contoso.local"
$ServerCertFriendly = "DirectoryAnalyzer Agent Server"
$ClientCertFriendly = "DirectoryAnalyzer Analyzer Client"
$ExportPath = "C:\Temp\DirectoryAnalyzer-Certs"

New-Item -ItemType Directory -Path $ExportPath -Force | Out-Null

# Server cert (TLS server)
$serverCert = New-SelfSignedCertificate \
  -DnsName $DnsName \
  -CertStoreLocation "Cert:\LocalMachine\My" \
  -FriendlyName $ServerCertFriendly \
  -KeyExportPolicy Exportable \
  -KeyLength 2048 \
  -KeyUsage DigitalSignature, KeyEncipherment \
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.1")

# Client cert (TLS client)
$clientCert = New-SelfSignedCertificate \
  -DnsName "analyzer-client" \
  -CertStoreLocation "Cert:\LocalMachine\My" \
  -FriendlyName $ClientCertFriendly \
  -KeyExportPolicy Exportable \
  -KeyLength 2048 \
  -KeyUsage DigitalSignature \
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.2")

# Export public certs (.cer)
Export-Certificate -Cert $serverCert -FilePath "$ExportPath\agent-server.cer" | Out-Null
Export-Certificate -Cert $clientCert -FilePath "$ExportPath\analyzer-client.cer" | Out-Null

# Export client cert as PFX for Analyzer host import (protect with password)
$clientPfxPassword = Read-Host -AsSecureString "Enter PFX password"
Export-PfxCertificate -Cert $clientCert -FilePath "$ExportPath\analyzer-client.pfx" -Password $clientPfxPassword | Out-Null

Write-Host "Server thumbprint:" $serverCert.Thumbprint
Write-Host "Client thumbprint:" $clientCert.Thumbprint
```

## 2) Install certs into correct stores

### 2.1 Agent host (server cert + trusted client cert)

> Run on the **Agent host** (PowerShell as Administrator).

```powershell
$CertPath = "C:\Temp\DirectoryAnalyzer-Certs"

# Import server cert into LocalMachine\My
Import-Certificate -FilePath "$CertPath\agent-server.cer" -CertStoreLocation "Cert:\LocalMachine\My" | Out-Null

# Trust client cert in LocalMachine\Root (lab only) or use an internal CA chain in production
Import-Certificate -FilePath "$CertPath\analyzer-client.cer" -CertStoreLocation "Cert:\LocalMachine\Root" | Out-Null

# Confirm
Get-ChildItem Cert:\LocalMachine\My | Where-Object {$_.Subject -like "*DirectoryAnalyzer Agent Server*"}
Get-ChildItem Cert:\LocalMachine\Root | Where-Object {$_.Subject -like "*DirectoryAnalyzer Analyzer Client*"}
```

### 2.2 Analyzer host (client cert + trusted server cert)

> Run on the **Analyzer host** (PowerShell as Administrator).

```powershell
$CertPath = "C:\Temp\DirectoryAnalyzer-Certs"

# Import client PFX into CurrentUser\My (or LocalMachine\My for service accounts)
$clientPfxPassword = Read-Host -AsSecureString "Enter PFX password"
Import-PfxCertificate -FilePath "$CertPath\analyzer-client.pfx" -CertStoreLocation "Cert:\CurrentUser\My" -Password $clientPfxPassword | Out-Null

# Trust server cert in LocalMachine\Root (lab only) or use an internal CA chain in production
Import-Certificate -FilePath "$CertPath\agent-server.cer" -CertStoreLocation "Cert:\LocalMachine\Root" | Out-Null

# Confirm
Get-ChildItem Cert:\CurrentUser\My | Where-Object {$_.Subject -like "*DirectoryAnalyzer Analyzer Client*"}
Get-ChildItem Cert:\LocalMachine\Root | Where-Object {$_.Subject -like "*DirectoryAnalyzer Agent Server*"}
```

## 3) Bind HTTPS cert to port (netsh) **or** configure Kestrel

### Option A: netsh HTTP.SYS binding (recommended for Windows Service)

> Run on **Agent host** (PowerShell as Administrator).

```powershell
$Port = 8443
$Thumbprint = "<agent-server-thumbprint>"  # from Step 1 output

# Clean existing binding (if needed)
netsh http delete sslcert ipport=0.0.0.0:$Port

# Bind cert to port
netsh http add sslcert ipport=0.0.0.0:$Port certhash=$Thumbprint appid='{B6C1E08F-6F0D-4D29-8AE1-DAF4B0C0A9A1}'

# Verify
netsh http show sslcert ipport=0.0.0.0:$Port
```

### Option B: Kestrel configuration (appsettings)

> Use this if you are **self-hosting** the agent without HTTP.SYS.

```json
// %ProgramData%\DirectoryAnalyzerAgent\agentsettings.json
{
  "Urls": "https://+:8443/agent/",
  "Kestrel": {
    "Endpoints": {
      "Https": {
        "Url": "https://+:8443/agent/",
        "Certificate": {
          "Subject": "DirectoryAnalyzer Agent Server",
          "Store": "My",
          "Location": "LocalMachine"
        }
      }
    }
  }
}
```

## 4) Install agent MSI, start service, verify running

> Run on the **Agent host** (PowerShell as Administrator).

```powershell
# MSI install
$MsiPath = "C:\Installers\DirectoryAnalyzer.Agent.msi"
$AgentThumbprint = "<agent-server-thumbprint>"
$ClientThumbprint = "<analyzer-client-thumbprint>"

msiexec /i $MsiPath /l*v C:\Temp\AgentInstall.log \
  SERVICEACCOUNT="CONTOSO\gmsaDirectoryAnalyzer$" SERVICEPASSWORD="" \
  CERTTHUMBPRINT="$AgentThumbprint" ANALYZERCLIENTTHUMBPRINTS="$ClientThumbprint" \
  BINDPREFIX="https://+:8443/agent/"

# Start service
Start-Service -Name DirectoryAnalyzerAgent

# Verify status
Get-Service -Name DirectoryAnalyzerAgent
```

## 5) Validate connectivity from Analyzer host

> Run on the **Analyzer host** (PowerShell).

```powershell
$AgentHost = "agent01.contoso.local"
$Port = 8443

# Quick TCP reachability
Test-NetConnection -ComputerName $AgentHost -Port $Port

# mTLS test (using client cert from CurrentUser\My)
$clientCert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -like "*DirectoryAnalyzer Analyzer Client*" } | Select-Object -First 1
Invoke-WebRequest -Uri "https://$AgentHost:$Port/agent/health" -Certificate $clientCert -UseBasicParsing
```

> Expected: HTTP 200 OK or a valid JSON health response, depending on endpoint implementation.

## 6) Troubleshooting checklist

**Certificates & stores**
- Confirm server cert is in **LocalMachine\My** on Agent host and has **Server Authentication** EKU.
- Confirm Analyzer client cert is in **CurrentUser\My** (or LocalMachine\My for service account) and has **Client Authentication** EKU.
- Validate trust chain: root/intermediate certs are in **LocalMachine\Root** on both hosts.

**HTTPS binding**
- `netsh http show sslcert ipport=0.0.0.0:8443` shows the expected thumbprint.
- Port is not already bound by another service.

**Firewall & network**
- Inbound TCP **8443** open on Agent host from Analyzer subnet only.
- `Test-NetConnection` succeeds from Analyzer host.

**Service & logs**
- `Get-Service DirectoryAnalyzerAgent` returns **Running**.
- Check Windows Event Viewer: **Application** and **System** logs for TLS or service errors.
- MSI log: `C:\Temp\AgentInstall.log` for install issues.

**Common errors**
- `The client and server cannot communicate, because they do not possess a common algorithm.` → TLS/Schannel policy mismatch; ensure TLS 1.2 enabled.
- `Could not establish trust relationship` → missing root/intermediate cert or wrong thumbprint.
- `HTTP 403` → client cert not allowed/whitelisted by agent config.
- `HTTP 404` → wrong URL path (`/agent/health`) or base path mismatch.
