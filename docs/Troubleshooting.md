# Troubleshooting

## Finalidade
Consolidar falhas comuns de build e runtime, com causas e correções comprovadas no código.

## Público-alvo
* Suporte
* Operações
* Infra/AD

## Premissas
* Ambiente Windows com módulos PowerShell e permissões adequadas.

## Falhas de build
### 1) Targeting pack do .NET Framework 4.8 ausente
**Sintoma:** erro de referência a assemblies do framework.
**Correção:** instalar .NET Framework 4.8 Developer Pack.

### 2) Pacotes NuGet não restaurados
**Correção:** executar restore via Visual Studio ou MSBuild.

## Falhas de runtime por módulo
### DNS Analyzer
* **Falha:** “Módulo DnsServer não encontrado.”
* **Correção:** instalar módulo `DnsServer`.
* **Provas:** `DnsCollector` valida módulo com `Get-Module DnsServer`.

### GPO Analyzer
* **Falha:** módulo `GroupPolicy` ausente.
* **Correção:** instalar RSAT / GPMC.

### SMB/Scheduled Tasks/Profiles/Services/IIS
* **Falha:** erro remoto (Invoke-Command).
* **Correção:** habilitar WinRM e firewall.

### Trusts/ProxyAddresses
* **Falha:** módulo `ActiveDirectory` ausente.
* **Correção:** instalar RSAT AD.

## Falhas do Agente
* **403 Forbidden:** thumbprint não está allowlist.
* **InvalidSignature:** assinatura inválida.
* **ReplayDetected:** nonce duplicado.
* **RequestExpired:** clock skew.
* **429 RateLimited:** excedeu rate limit.

## Checklist de diagnóstico (PowerShell)
```powershell
# Verificar módulos
Get-Module -ListAvailable ActiveDirectory
Get-Module -ListAvailable DnsServer
Get-Module -ListAvailable GroupPolicy
Get-Module -ListAvailable WebAdministration

# Verificar WinRM
Test-WSMan <servidor>

# Verificar certificados
Get-ChildItem Cert:\LocalMachine\My
Get-ChildItem Cert:\CurrentUser\My

# Verificar porta do agente
Test-NetConnection <host-agente> -Port 8443
```

## Ponteiros de código (provas)
* DNS Analyzer: `DnsCollector`.
* Execução remota: `SmbAnalyzerView.xaml.cs`, `ScheduledTasksAnalyzerView.xaml.cs`.
* Agente: `AgentHost`, `AgentRequestSigner`.

## LIMITAÇÕES ATUAIS
* Não há utilitário embutido de diagnóstico automatizado; usa logs e comandos manuais.

## COMO VALIDAR
1. Reproduzir falha.
2. Coletar log do módulo.
3. Aplicar correção e reexecutar.
