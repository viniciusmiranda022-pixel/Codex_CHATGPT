# Troubleshooting

## Build
### .NET Framework 4.8 targeting pack ausente
Sintoma, erros de referência do framework.
Correção, instalar .NET Framework 4.8 Developer Pack.

### Pacotes NuGet não restaurados
Sintoma, erro de referência de pacote.
Correção, executar restore no Visual Studio ou MSBuild.

## Módulos PowerShell e RSAT
### DNS Analyzer
Sintoma, módulo `DnsServer` não encontrado.
Correção, instalar o módulo `DnsServer`.

### GPO Analyzer
Sintoma, módulo `GroupPolicy` ausente.
Correção, instalar RSAT ou GPMC.

### Trusts e ProxyAddresses
Sintoma, módulo `ActiveDirectory` ausente.
Correção, instalar RSAT AD.

### IIS AppPools
Sintoma, módulo `WebAdministration` ausente.
Correção, instalar IIS e o módulo.

## WinRM e execução remota
Sintoma, falhas em SMB Shares, Scheduled Tasks, Local Profiles, Installed Services ou Local Security Policy.
Correção, habilitar WinRM e liberar firewall no destino.

## Agente
### 403 Forbidden
Causa provável, thumbprint do cliente fora da allowlist.
Correção, atualizar `AnalyzerClientThumbprints` em `%ProgramData%\DirectoryAnalyzerAgent\agentsettings.json`.

### InvalidSignature
Causa provável, assinatura inválida ou certificado diferente do configurado.
Correção, validar thumbprint do cliente em `%ProgramData%\DirectoryAnalyzerAgent\agentclientsettings.json`.

### ReplayDetected ou RequestExpired
Causa provável, relógio com skew acima do permitido ou nonce repetido.
Correção, sincronizar horário com NTP e reenviar.

### 429 RateLimited
Causa provável, excesso de chamadas por minuto.
Correção, reduzir frequência ou ajustar `MaxRequestsPerMinute`.

### 404 ou 403 no endpoint
Causa provável, endpoint errado ou TLS não configurado.
Correção, validar `BindPrefix` e binding HTTPS.

## Comandos úteis
```powershell
# Módulos PowerShell
Get-Module -ListAvailable ActiveDirectory
Get-Module -ListAvailable DnsServer
Get-Module -ListAvailable GroupPolicy
Get-Module -ListAvailable WebAdministration

# WinRM
Test-WSMan <servidor>

# Certificados
Get-ChildItem Cert:\LocalMachine\My
Get-ChildItem Cert:\CurrentUser\My

# Porta do agente
Test-NetConnection <host-agente> -Port 8443

# Serviço do agente
Get-Service DirectoryAnalyzerAgent
```
