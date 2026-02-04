# Fase 0 — Baseline & Diagnóstico

## Inventário rápido

### Solution(s)
- `DirectoryAnalyzer.sln`

### Projetos (.csproj)
- `DirectoryAnalyzer` (WPF UI) — `DirectoryAnalyzer.csproj`
- `DirectoryAnalyzer.SmokeTests` — `DirectoryAnalyzer.SmokeTests/DirectoryAnalyzer.SmokeTests.csproj`
- `AgentService` — `AgentService/AgentService.csproj`
- `AnalyzerClient` — `AnalyzerClient/AnalyzerClient.csproj`
- `DirectoryAnalyzer.Configuration.Tests` — `DirectoryAnalyzer.Configuration.Tests/DirectoryAnalyzer.Configuration.Tests.csproj`
- `DirectoryAnalyzer.Agent.Contracts` — `DirectoryAnalyzer.Agent.Contracts/DirectoryAnalyzer.Agent.Contracts.csproj`
- `DirectoryAnalyzer.Agent` — `DirectoryAnalyzer.Agent/DirectoryAnalyzer.Agent.csproj`
- `DirectoryAnalyzer.Agent.Client` — `DirectoryAnalyzer.Agent.Client/DirectoryAnalyzer.Agent.Client.csproj`

### Frameworks alvo
- `.NET Framework 4.8` / `net48` em todos os projetos.

### Pontos de entrada
- WPF UI: `App.xaml` (StartupUri `MainWindow.xaml`).
- Console/Service:
  - `AgentService/Program.cs`
  - `AnalyzerClient/Program.cs`
  - `DirectoryAnalyzer.Agent/Program.cs`
  - `DirectoryAnalyzer.SmokeTests/Program.cs`

## Locais com Agent Mode / caminhos sem agente
- `ViewModels/AgentsViewModel.cs` — referência a `AgentModeSettings`.
- `Services/AgentSettings.cs` — definição/serialização `AgentModeSettings`.
- `docs/Arquitetura.md` — referência a `AgentSettingsStore` e “sem toggle de Agent Mode”.

> Observação: os locais acima devem ser eliminados na fase 5 conforme a regra “agent-only”, removendo toggle e qualquer caminho alternativo sem agente.

## Arquivos “ruído” no repositório (não apagar ainda)

### Itens soltos na raiz do repo
- Muitos arquivos XAML/C# de Views e ViewModels diretamente na raiz.
- Diretórios de domínio/estrutura misturados (`Models`, `Modules`, `Services`, `Themes`, etc.).

### Arquivos temporários/versionados
- `DirectoryAnalyzer.csproj.Backup.tmp` (precisa ser removido na fase 1 e coberto pelo `.gitignore`).

### Duplicidade de documentação
- `DOCS/` e `docs/` coexistem.
- `README.md` na raiz e documentos potencialmente redundantes entre `DOCS/` e `docs/`.

## Plano de movimentação (prévia)

### Reorganização (Fase 1)
- Criar estrutura `src/` e mover projetos/arquivos para:
  - `src/DirectoryAnalyzer.UI`
  - `src/DirectoryAnalyzer.Contracts`
  - `src/DirectoryAnalyzer.Broker`
  - `src/DirectoryAnalyzer.Agent`
  - `src/DirectoryAnalyzer.Agent.Client`
  - `src/DirectoryAnalyzer.Core`
  - `src/DirectoryAnalyzer.Collectors`
  - `src/DirectoryAnalyzer.Infrastructure`
  - `src/Installer` (se mantido)
- Consolidar documentação em `docs/` e mover duplicados para `docs/legacy/`.
- Remover arquivos temporários e reforçar `.gitignore` para `bin/obj/*.tmp`.

### Limpeza arquitetural (Fases 2–7)
- Centralizar DTOs em `DirectoryAnalyzer.Contracts`.
- Introduzir `DirectoryAnalyzer.Broker` (ASP.NET Core) como único ponto de acesso da UI.
- Refatorar coletas para `DirectoryAnalyzer.Collectors` e execução via Agent.
- Atualizar UI para MVVM padronizado e integração exclusiva com Broker.

