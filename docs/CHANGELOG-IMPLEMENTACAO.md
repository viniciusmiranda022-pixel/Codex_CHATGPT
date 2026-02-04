# Changelog de Implementação

## Inventário rápido (estado atual)

### Solutions e projetos (.csproj)
- `DirectoryAnalyzer.sln`
- `DirectoryAnalyzer.UI` (WPF UI) — `src/DirectoryAnalyzer.UI/DirectoryAnalyzer.UI.csproj`
- `DirectoryAnalyzer.SmokeTests` — `src/DirectoryAnalyzer.SmokeTests/DirectoryAnalyzer.SmokeTests.csproj`
- `AgentService` — `src/AgentService/AgentService.csproj`
- `AnalyzerClient` — `src/AnalyzerClient/AnalyzerClient.csproj`
- `DirectoryAnalyzer.Configuration.Tests` — `src/DirectoryAnalyzer.Configuration.Tests/DirectoryAnalyzer.Configuration.Tests.csproj`
- `DirectoryAnalyzer.Agent.Contracts` — `src/DirectoryAnalyzer.Agent.Contracts/DirectoryAnalyzer.Agent.Contracts.csproj`
- `DirectoryAnalyzer.Agent` — `src/DirectoryAnalyzer.Agent/DirectoryAnalyzer.Agent.csproj`
- `DirectoryAnalyzer.Agent.Client` — `src/DirectoryAnalyzer.Agent.Client/DirectoryAnalyzer.Agent.Client.csproj`

### Frameworks alvo
- Todos os projetos direcionam `.NET Framework 4.8` (`net48`/`v4.8`).

## Onde estão as coletas atuais

### PowerShellService e coletores C#
- `Services/PowerShellService.cs` centraliza a execução de PowerShell e sanitização.
- `Modules/Dns/DnsCollector.cs` usa `PowerShellService` e AD (Get-AD*).

### Code-behind com coleta direta na UI (PowerShell/WinRM/CIM)
- `ScheduledTasksAnalyzerView.xaml.cs`
- `SmbAnalyzerView.xaml.cs`
- `InstalledServicesAnalyzerView.xaml.cs`
- `LocalProfilesAnalyzerView.xaml.cs`
- `LocalSecurityPolicyAnalyzerView.xaml.cs`
- `IisAnalyzerView.xaml.cs`
- `TrustAnalyzerView.xaml.cs`
- `ProxyAddressAnalyzerView.xaml.cs`
- `GpoAnalyzerView.xaml.cs`

## Referências a AgentModeEnabled ou coleta direta na UI
- `ViewModels/AgentsViewModel.cs` — referência a `AgentModeSettings`.
- `Services/AgentSettings.cs` — definição/serialização de `AgentModeSettings`.

> Observação: esta área será removida/consolidada conforme a regra agent-only.

## Arquivos temporários versionados (marcar para remoção na Fase 1)
- Removido: `DirectoryAnalyzer.csproj.Backup.tmp` (não deve mais existir no repo).
