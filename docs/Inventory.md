# Inventário inicial (Fase 0)

## Projetos e frameworks
| Projeto | Caminho | Target | Observações |
| --- | --- | --- | --- |
| DirectoryAnalyzer (UI WPF) | `DirectoryAnalyzer.csproj` | net48 | WPF principal com XAML e módulos. |
| DirectoryAnalyzer.Agent | `DirectoryAnalyzer.Agent/DirectoryAnalyzer.Agent.csproj` | net48 | Host do agente (console/worker). |
| DirectoryAnalyzer.Agent.Client | `DirectoryAnalyzer.Agent.Client/DirectoryAnalyzer.Agent.Client.csproj` | net48 | Cliente HTTP do agente usado pela UI. |
| DirectoryAnalyzer.Agent.Contracts | `DirectoryAnalyzer.Agent.Contracts/DirectoryAnalyzer.Agent.Contracts.csproj` | net48 | Contratos atuais do agente. |
| AgentService | `AgentService/AgentService.csproj` | net48 | Implementação alternativa do host do agente. |
| AnalyzerClient | `AnalyzerClient/AnalyzerClient.csproj` | .NET Framework v4.8 | Console para integração com agente. |
| DirectoryAnalyzer.SmokeTests | `DirectoryAnalyzer.SmokeTests/DirectoryAnalyzer.SmokeTests.csproj` | net48 | Smoke tests existentes. |
| DirectoryAnalyzer.Configuration.Tests | `DirectoryAnalyzer.Configuration.Tests/DirectoryAnalyzer.Configuration.Tests.csproj` | net48 | Tests de configuração. |
| DirectoryAnalyzer.Agent.Installer | `Installer/DirectoryAnalyzer.Agent.wixproj` | WiX | Instalador do agente. |

## Serviços principais e pontos de extensão
- **PowerShellService**: execução de scripts e captura de saída/erros. (`Services/PowerShellService.cs`)
- **LogService**: logging por módulo e dashboard. (`Services/LogService.cs`, `Services/DashboardService.cs`)
- **ExportService**: exportações CSV/XML/HTML/SQL. (`ExportService.cs`)
- **AgentClientService**: wrapper de cliente do agente para a UI. (`Services/AgentClientService.cs`)
- **Coletores**: classes em `Modules/*` com coleta específica (DNS, GPO, SMB, etc.).

## Resíduos identificados para limpeza posterior (após build ok)
- `DirectoryAnalyzer.csproj.Backup.tmp` (backup temporário no root).
- Pasta `Arquivos de base/` (verificar uso em build e consolidar em docs/legacy ou remover).
- `DOCS/` e `docs/` duplicados (consolidar documentação depois da reorganização da solução).
