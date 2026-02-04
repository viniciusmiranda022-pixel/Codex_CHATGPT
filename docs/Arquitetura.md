# Arquitetura

## Finalidade
Documentar a arquitetura real do repositório, a estrutura da solução e como os módulos se conectam, com **provas em código** para cada afirmação.

## Público-alvo
* Arquitetura/Engenharia
* Infraestrutura Windows/AD
* Segurança
* Desenvolvimento

## Premissas
* A solução é **.NET Framework 4.8**.
* A UI principal é WPF.
* O agente é uma aplicação Windows console/service com HTTPS via `HttpListener`.

## Inventário da solução (.sln)
**Arquivo de solução:** `DirectoryAnalyzer.sln`

| Projeto | Tipo | Output | Responsabilidade | Dependências |
| --- | --- | --- | --- | --- |
| DirectoryAnalyzer | WPF | `DirectoryAnalyzer.exe` | App principal e UI | `DirectoryAnalyzer.Agent.Client`, `DirectoryAnalyzer.Agent.Contracts` |
| DirectoryAnalyzer.SmokeTests | Console | `DirectoryAnalyzer.SmokeTests.exe` | Smoke tests (PowerShell + recursos WPF) | `DirectoryAnalyzer` (uso de Services) |
| AgentService | Console/Service | `DirectoryAnalyzer.Agent.exe` | Host do agente (HttpListener) | `DirectoryAnalyzer.Agent.Contracts` |
| AnalyzerClient | Console | `DirectoryAnalyzer.AnalyzerClient.exe` | Cliente de teste do agente | `DirectoryAnalyzer.Agent.Contracts` |
| DirectoryAnalyzer.Agent.Installer | WiX | `DirectoryAnalyzer.Agent.msi` | Instalador do agente | `AgentService` output |
| DirectoryAnalyzer.Agent.Contracts | Class Library | `DirectoryAnalyzer.Agent.Contracts.dll` | DTOs e assinatura | N/A |
| DirectoryAnalyzer.Agent | Console/Service | `DirectoryAnalyzer.Agent.exe` | Host do agente (SDK style) | `DirectoryAnalyzer.Agent.Contracts` |
| DirectoryAnalyzer.Agent.Client | Class Library | `DirectoryAnalyzer.Agent.Client.dll` | Cliente do agente usado pela UI | `DirectoryAnalyzer.Agent.Contracts` |

**Provas (ponteiros de código):**
* Solução e projetos: `DirectoryAnalyzer.sln`.
* `DirectoryAnalyzer.csproj` (WPF, net48).
* `AgentService.csproj`, `DirectoryAnalyzer.Agent.csproj` (net48).

## Diagrama ASCII (fluxo geral)
```
+----------------------------+     +---------------------------+
| WPF (DirectoryAnalyzer)    |     | Agent (DirectoryAnalyzer) |
| - Views/ViewModels         |     | - HttpListener HTTPS       |
| - PowerShellService        |<--->| - ActionRegistry (LDAP)    |
| - ExportService            |     | - AgentLogger              |
+-------------+--------------+     +---------------------------+
              |
              | (PowerShell / AD / CIM local e remoto)
              v
+----------------------------+
| Infra Windows/AD            |
+----------------------------+
```

## Arquitetura da UI (WPF)
### Shell e Navegação
* `MainWindow.xaml` define menu lateral + `ContentControl`.
* `MainViewModel` cria um **factory map** de views e troca o `CurrentView` conforme a navegação.

**Provas:** `MainWindow.xaml`, `MainWindow.xaml.cs`, `MainViewModel`.

### MVVM vs Code-behind
* **MVVM completo** no DNS Analyzer: `DnsAnalyzerViewModel` + `DnsCollector`.
* **Code-behind** nos demais módulos: `*.xaml.cs` invocando `PowerShellService`.

**Provas:** `DnsAnalyzerViewModel`, `Modules/Dns/DnsCollector.cs`, `GpoAnalyzerView.xaml.cs`, `SmbAnalyzerView.xaml.cs`, etc.

## Camadas de serviço
### PowerShellService
* Responsável por execução de scripts PowerShell, sanitização de parâmetros sensíveis e logging.
* Implementa `ExecuteScriptAsync` e `ExecuteScriptWithResultAsync`.

**Provas:** `Services/PowerShellService.cs`.

### LogService
* Cria logs por módulo e por execução.
* Logs gravados em `%LocalAppData%\DirectoryAnalyzer\Logs\<ModuleName>`.

**Provas:** `Services/LogService.cs`.

### ExportService
* Exporta resultados para CSV/XML/HTML/SQL Server.
* Sanitiza nomes de colunas e tabelas no SQL.

**Provas:** `ExportService.cs`.

## Configuração e carregamento
* **Agent Mode (UI):** `AgentSettingsStore` resolve `agentclientsettings.json`.
* **Agente:** `AgentConfigLoader` carrega `agentsettings.json` e sobrescreve via registry.

**Provas:** `Services/AgentSettings.cs`, `DirectoryAnalyzer.Agent.Contracts/AgentConfig.cs`.

## Ponteiros de código (provas)
* Navegação e shell: `MainWindow.xaml`, `MainViewModel`.
* Execução PowerShell: `Services/PowerShellService.cs`.
* Logs e dashboard: `Services/LogService.cs`, `Services/DashboardService.cs`.
* Exportação: `ExportService.cs`.

## LIMITAÇÕES ATUAIS
* **Duas implementações do host do agente** (AgentService e DirectoryAnalyzer.Agent) coexistem, o que pode confundir manutenção.

## COMO VALIDAR
1. Abrir `DirectoryAnalyzer.sln` e confirmar todos os projetos listados.
2. Compilar `DirectoryAnalyzer` e executar navegação entre views.
3. Validar logs em `%LocalAppData%\DirectoryAnalyzer\Logs`.
4. Executar `DirectoryAnalyzer.Agent.exe` e `AnalyzerClient` para validação do fluxo do agente.
