# Coleta de Dados

## Finalidade
Descrever, com fidelidade ao código, **como cada módulo/analyzer coleta dados**, quais tecnologias/protocolos são utilizados, os campos de saída e os modos de falha.

## Público-alvo
* Infra/AD
* Segurança
* Operações
* Desenvolvimento

## Premissas
* A coleta é **read-only** e se baseia em PowerShell, LDAP/DirectoryServices e CIM/WMI.
* Vários módulos executam **Invoke-Command** remoto e, portanto, dependem de WinRM.

## Visão geral: coleta local vs. remota
* **Local (padrão):** Scripts PowerShell executados no host da UI (DirectoryAnalyzer).
* **Remota (opcional):** Agente HTTPS com mTLS e assinatura de requisições.

**Provas:** `PowerShellService`, `DnsCollector`, `AgentHost`, `ActionRegistry`.

## Módulos / analyzers (todos existentes)

> Convenções usadas abaixo:
> * **Portas/protocolos** são separadas entre “Windows padrão” e “explicitamente usado no código”.
> * **Esquema de saída** é baseado nos `PSCustomObject` e/ou modelos C#.

### 1) DNS Analyzer
**O que coleta:** Zonas, registros e encaminhadores DNS do PDC.

**De onde coleta:** PowerShell (`DnsServer` + `ActiveDirectory`).

**Permissões mínimas:** leitura AD e permissão de consulta DNS no DC (read-only).

**Portas/protocolos:**
* Windows padrão: LDAP/GC para AD (depende do módulo), RPC/SMB internos do DC.
* Explicitamente no código: `Get-DnsServer*` via PowerShell.

**Esquema de saída:**
* `DnsZoneResult`: `ZoneName`, `ZoneType`, `IsReverseLookupZone`, `DynamicUpdate`.
* `DnsRecordResult`: `ZoneName`, `HostName`, `RecordType`, `TimeToLive`, `RecordData`.
* `DnsForwarderResult`: `IPAddress`, `UseRootHint`, `EnableReordering`, `Timeout`.

**Falhas típicas:** módulo `DnsServer` ausente ou falta de permissão.

**Performance:** coleta sequencial; varre todas as zonas e registros do PDC.

**Provas:** `Modules/Dns/DnsCollector.cs`, `Models/Dns*.cs`.

---

### 2) GPO Analyzer
**O que coleta:** GPOs, links, delegação, filtros de segurança e WMI.

**De onde coleta:** PowerShell (`GroupPolicy` + `ActiveDirectory`).

**Permissões mínimas:** leitura de GPOs no domínio.

**Portas/protocolos:**
* Windows padrão: LDAP/RPC para AD e GPO.
* Explicitamente no código: `Get-GPO`, `Get-GPOReport`, `Get-GPPermission`.

**Esquema de saída (PSCustomObject):**
* `Resumo`: `Nome`, `GUID`, `Status`, `CriadoEm`, `ModificadoEm`, `FiltroWMINome`.
* `Links`: `GPO_Nome`, `VinculadoEm_DN`, `LinkHabilitado`, `LinkForcado`.
* `Delegacoes`: `GPO_Nome`, `Trustee`, `Permissao`.
* `SecurityFiltering`: `GPO_Nome`, `FiltroSeguranca`, `PermissaoFiltro`.
* `WmiFilters`: `GPO_Nome`, `GPO_GUID`, `WMIFilterNome`, `WMIQuery`.

**Falhas típicas:** módulo `GroupPolicy` ausente, permissões insuficientes.

**Performance:** varre todos os GPOs, gera reports XML por GPO.

**Provas:** `GpoAnalyzerView.xaml.cs`.

---

### 3) SMB Shares Analyzer
**O que coleta:** Compartilhamentos SMB e permissões NTFS.

**De onde coleta:** PowerShell com `ActiveDirectory` + `Get-CimInstance Win32_Share` + `Invoke-Command` remoto.

**Permissões mínimas:** leitura AD e permissão de leitura de ACL nos servidores remotos.

**Portas/protocolos:**
* Windows padrão: LDAP/GC (AD), ICMP (ping), SMB/RPC internos.
* Explicitamente no código: WinRM (Invoke-Command), CIM/WMI.

**Esquema de saída:** `ComputerName`, `ShareName`, `SharePath`, `IdentityReference`, `AccessControlType`, `FileSystemRights`, `IsInherited`.

**Falhas típicas:** WinRM desabilitado, firewall, ACLs negadas.

**Performance:** itera servidores sequencialmente; sem paralelismo.

**Provas:** `SmbAnalyzerView.xaml.cs`.

---

### 4) Scheduled Tasks Analyzer
**O que coleta:** Tarefas agendadas fora de `\Microsoft*`.

**De onde coleta:** PowerShell + `ActiveDirectory` + `Invoke-Command`.

**Permissões mínimas:** leitura AD e permissão para listar tarefas nos hosts.

**Portas/protocolos:**
* Windows padrão: LDAP/GC (AD), ICMP.
* Explicitamente no código: WinRM.

**Esquema de saída:** `ComputerName`, `State`, `TaskName`, `UserId`, `TaskPath`.

**Falhas típicas:** WinRM desabilitado, falta de permissões.

**Performance:** coleta sequencial por servidor.

**Provas:** `ScheduledTasksAnalyzerView.xaml.cs`.

---

### 5) Local Profiles Analyzer
**O que coleta:** Perfis locais (`Win32_UserProfile`).

**De onde coleta:** PowerShell + `ActiveDirectory` + CIM/WMI.

**Permissões mínimas:** leitura AD e permissão WMI nos servidores.

**Portas/protocolos:**
* Windows padrão: LDAP/GC.
* Explicitamente no código: CIM/WMI remoto.

**Esquema de saída:** `ComputerName`, `AccountName`, `SID`, `LocalPath`, `LastUseTime`.

**Falhas típicas:** WMI remoto bloqueado, permissões insuficientes.

**Performance:** sequencial por servidor.

**Provas:** `LocalProfilesAnalyzerView.xaml.cs`.

---

### 6) Service Account Analyzer (Installed Services)
**O que coleta:** Serviços Windows e contas de serviço.

**De onde coleta:** PowerShell + `ActiveDirectory` + `Invoke-Command` com `Win32_Service`.

**Permissões mínimas:** leitura AD e permissão para consultar serviços.

**Portas/protocolos:**
* Windows padrão: LDAP/GC.
* Explicitamente no código: WinRM.

**Esquema de saída:** `ComputerName`, `DisplayName`, `Name`, `State`, `StartMode`, `ContaDeServico`, `PathName`, `Description`, `ServiceType`.

**Falhas típicas:** WinRM desabilitado, acesso negado.

**Performance:** sequencial por servidor.

**Provas:** `InstalledServicesAnalyzerView.xaml.cs`.

---

### 7) Local Security Policy Analyzer
**O que coleta:** Configuração local via `secedit /export`.

**De onde coleta:** PowerShell remoto, exportando INF e parseando conteúdo.

**Permissões mínimas:** permissão de execução remota e leitura de política.

**Portas/protocolos:**
* Windows padrão: LDAP/GC.
* Explicitamente no código: WinRM.

**Esquema de saída:** `ComputerName`, `PolicyArea`, `SettingName`, `SettingValue`, `Descricao`.

**Falhas típicas:** secedit indisponível, WinRM bloqueado.

**Performance:** sequencial por servidor; parse linha a linha.

**Provas:** `LocalSecurityPolicyAnalyzerView.xaml.cs`.

---

### 8) IIS AppPools Analyzer
**O que coleta:** AppPools e sites vinculados.

**De onde coleta:** PowerShell + `WebAdministration` remoto.

**Permissões mínimas:** leitura IIS nos hosts.

**Portas/protocolos:**
* Windows padrão: LDAP/GC.
* Explicitamente no código: WinRM.

**Esquema de saída:** `ComputerName`, `ApplicationPool`, `Status`, `CustomIdentity`, `SitesVinculados`.

**Falhas típicas:** IIS não instalado, módulo `WebAdministration` ausente, WinRM bloqueado.

**Performance:** sequencial por servidor.

**Provas:** `IisAnalyzerView.xaml.cs`.

---

### 9) Trusts Analyzer
**O que coleta:** Relações de confiança AD (`Get-ADTrust`).

**De onde coleta:** PowerShell local com módulo `ActiveDirectory`.

**Permissões mínimas:** leitura de trusts no AD.

**Portas/protocolos:**
* Windows padrão: LDAP/GC.
* Explicitamente no código: `Get-ADTrust`.

**Esquema de saída:** `Source`, `Target`, `Direction`, `TrustType`, `IsTransitive`.

**Falhas típicas:** módulo AD ausente.

**Performance:** consulta única.

**Provas:** `TrustAnalyzerView.xaml.cs`.

---

### 10) ProxyAddresses Analyzer
**O que coleta:** ProxyAddresses de usuários filtrados.

**De onde coleta:** PowerShell `Get-ADUser` com filtro e propriedade `ProxyAddresses`.

**Permissões mínimas:** leitura AD.

**Portas/protocolos:**
* Windows padrão: LDAP/GC.
* Explicitamente no código: `Get-ADUser`.

**Esquema de saída:** `UserPrincipalName`, `SamAccountName`, `ProxyAddress`, `IsPrimarySmtp`.

**Falhas típicas:** módulo AD ausente, filtro inválido.

**Performance:** varre usuários do filtro e expande `ProxyAddresses`.

**Provas:** `ProxyAddressAnalyzerView.xaml.cs`.

---

### 11) Agent Inventory
**O que coleta:** Usuários via agente (`GetUsers`).

**De onde coleta:** HTTPS/mTLS para agente, que usa `DirectoryServices`.

**Permissões mínimas:** no agente, leitura AD no domínio configurado.

**Portas/protocolos:**
* Windows padrão: LDAP/GC no servidor do agente.
* Explicitamente no código: HTTPS (`HttpListener`), TLS 1.2.

**Esquema de saída:** `SamAccountName`, `DisplayName`, `Enabled`, `DistinguishedName`, `UserPrincipalName`, `ObjectSid`.

**Falhas típicas:** certificado de cliente inválido, allowlist, assinatura inválida.

**Performance:** limitado por rate limit e timeout.

**Provas:** `AgentInventoryViewModel`, `DirectoryAnalyzer.Agent.Client/AgentClient.cs`, `ActionRegistry`.

---

### 12) Agents (Configuração de agentes)
**O que faz:** gerencia endpoints, thumbprints e timeouts da UI.

**De onde coleta:** arquivo `agentclientsettings.json`.

**Falhas típicas:** JSON inválido ou ausência de arquivo (gera default).

**Provas:** `AgentsViewModel`, `AgentSettingsStore`.

---

### 13) Dashboard
**O que faz:** registra atividade recente dos módulos.

**Persistência:** `%LocalAppData%\DirectoryAnalyzer\recent.json`.

**Provas:** `DashboardService`.

## Ponteiros de código (provas)
* Coleta DNS: `Modules/Dns/DnsCollector.cs`, `DnsAnalyzerViewModel`.
* PowerShell remoto: `SmbAnalyzerView.xaml.cs`, `ScheduledTasksAnalyzerView.xaml.cs`, `LocalProfilesAnalyzerView.xaml.cs`, `InstalledServicesAnalyzerView.xaml.cs`, `LocalSecurityPolicyAnalyzerView.xaml.cs`, `IisAnalyzerView.xaml.cs`.
* Agente: `AgentHost`, `ActionRegistry`, `AgentContracts`.
* Configurações de agentes: `AgentSettingsStore`.
## LIMITAÇÕES ATUAIS
* Os módulos com code-behind não seguem MVVM completo (difícil testabilidade).
* A coleta remota usa execução sequencial; não há paralelismo explícito no código.

## COMO VALIDAR
1. Executar cada módulo e confirmar geração de resultados.
2. Validar exportações (CSV/XML/HTML/SQL).
3. Verificar logs por módulo.
4. Para módulos remotos, validar WinRM com `Test-WSMan`.
