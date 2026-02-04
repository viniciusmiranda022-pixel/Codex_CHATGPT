# AUDITORIA DE VERACIDADE

## Finalidade
Verificar a aderência entre a documentação e o código-fonte, registrando provas ou lacunas.

## Público-alvo
* Segurança
* Arquitetura
* Engenharia

## Premissas
* Auditoria baseada nos arquivos do repositório.

## Ponteiros de código (provas)
* `DirectoryAnalyzer.sln` e projetos referenciados.
* Classes e módulos citados nas afirmações abaixo.

## Metodologia
Para cada afirmação: **PROVADO** com ponteiro de código **ou** **NÃO VERIFICADO** com motivo.

## Afirmações auditadas (>= 25)
1) **O app principal é WPF e usa net48.**
   * PROVADO: `DirectoryAnalyzer.csproj` (`<TargetFramework>net48</TargetFramework>`, `<UseWPF>true</UseWPF>`).

2) **A navegação do menu usa `MainViewModel` e `MainWindow.xaml`.**
   * PROVADO: `MainWindow.xaml`, `MainViewModel`.

3) **DNS Analyzer usa MVVM completo.**
   * PROVADO: `DnsAnalyzerViewModel`, `DnsAnalyzerView.xaml.cs`.

4) **PowerShell é o mecanismo de execução de scripts.**
   * PROVADO: `Services/PowerShellService.cs`.

5) **Logs por módulo são gravados em `%LocalAppData%\DirectoryAnalyzer\Logs`.**
   * PROVADO: `LogService`.

6) **Dashboard grava `recent.json` em `%LocalAppData%\DirectoryAnalyzer`.**
   * PROVADO: `DashboardService`.

7) **Exportações CSV/XML/HTML/SQL são centralizadas em `ExportService`.**
   * PROVADO: `ExportService.cs`.

8) **Agente usa HTTPS obrigatório.**
   * PROVADO: `AgentHost` valida `IsSecureConnection`.

9) **Agente faz allowlist por thumbprint.**
   * PROVADO: `AgentConfig.AnalyzerClientThumbprints`, `ValidateClientCertificateAsync`.

10) **Agente verifica assinatura de requisição.**
   * PROVADO: `AgentRequestSigner.VerifySignature`.

11) **Anti-replay usa nonce e janela de tempo.**
   * PROVADO: `ValidateAntiReplay`, `NonceCache`.

12) **Rate limiting implementa sliding window + burst.**
   * PROVADO: `SlidingWindowRateLimiter`.

13) **MaxRequestBytes limita tamanho de payload.**
   * PROVADO: `AgentHost` check de `ContentLength64`.

14) **Ações do agente são allow-listed.**
   * PROVADO: `ActionRegistry` (dicionário de ações).

15) **GetUsers retorna `UserRecord` com SID e UPN.**
   * PROVADO: `AgentContracts` e `ActionRegistry.GetUsersAsync`.

16) **GPO Analyzer usa `Get-GPO` e `Get-GPOReport`.**
   * PROVADO: `GpoAnalyzerView.xaml.cs`.

17) **SMB Analyzer usa `Get-CimInstance Win32_Share`.**
   * PROVADO: `SmbAnalyzerView.xaml.cs`.

18) **Scheduled Tasks usa `Get-ScheduledTask`.**
   * PROVADO: `ScheduledTasksAnalyzerView.xaml.cs`.

19) **Local Profiles usa `Win32_UserProfile`.**
   * PROVADO: `LocalProfilesAnalyzerView.xaml.cs`.

20) **Local Security Policy usa `secedit /export`.**
   * PROVADO: `LocalSecurityPolicyAnalyzerView.xaml.cs`.

21) **IIS AppPools usa `WebAdministration`.**
   * PROVADO: `IisAnalyzerView.xaml.cs`.

22) **Trusts usa `Get-ADTrust`.**
   * PROVADO: `TrustAnalyzerView.xaml.cs`.

23) **ProxyAddresses usa `Get-ADUser` com ProxyAddresses.**
   * PROVADO: `ProxyAddressAnalyzerView.xaml.cs`.

24) **Agent Inventory UI usa `DirectoryAnalyzer.Agent.Client`.**
   * PROVADO: `AgentInventoryViewModel`.

25) **AnalyzerClient valida hostname do servidor.**
   * PROVADO: `AnalyzerClient/Program.cs` (`ValidateServerHostname`).

26) **AgentConfig pode ser sobrescrita via registry.**
   * PROVADO: `AgentConfigLoader.Load` (aplica overrides do registry).

27) **Instalador WiX grava `agentsettings.json` em `%ProgramData%\DirectoryAnalyzerAgent`.**
   * PROVADO: `Installer/README.md`, `Product.wxs` (CustomAction CreateAgentConfig).

28) **O resolver do agente procura `agentsettings.json` em `%ProgramData%\DirectoryAnalyzerAgent`.**
   * PROVADO: `DirectoryAnalyzer.Agent/Program.cs` e `AgentService/Program.cs`.

## Contradições encontradas
* Nenhuma contradição ativa sobre paths do agente, instalação e resolução usam `%ProgramData%\DirectoryAnalyzerAgent`.

## LIMITAÇÕES ATUAIS
* Alguns fluxos operacionais (ex.: geração automática de certificados) não existem no código; dependem de procedimento externo.

## COMO VALIDAR
* Conferir manualmente os ponteiros de código listados e repetir a auditoria após alterações.
