# Operação

## Finalidade
Descrever o ciclo operacional (build, execução, logs, config, upgrade/rollback) de forma fiel ao código.

## Público-alvo
* Operações
* Infra/AD
* Suporte

## Premissas
* Ambiente Windows com .NET Framework 4.8 e PowerShell.

## Build
### Visual Studio
1. Abrir `DirectoryAnalyzer.sln`.
2. Restaurar pacotes.
3. Compilar e executar `DirectoryAnalyzer`.

### CLI (PowerShell)
```powershell
msbuild .\DirectoryAnalyzer.sln /t:Restore,Build /p:Configuration=Debug
```

## Execução — logs
* Logs por módulo: `%LocalAppData%\DirectoryAnalyzer\Logs\<ModuleName>\<ModuleName>_yyyyMMdd_HHmmss.log`.
* Dashboard: `%LocalAppData%\DirectoryAnalyzer\recent.json`.

**Provas:** `LogService`, `DashboardService`.

## Execução — configurações
### Agent Mode (UI)
* `agentclientsettings.json` em `%ProgramData%\DirectoryAnalyzerAgent\` (preferencial) ou base dir.

### Agente (service/console)
* `agentsettings.json` em `%ProgramData%\DirectoryAnalyzer\` (preferencial) ou base dir.
* Overrides no registry: `HKLM\SOFTWARE\DirectoryAnalyzer\Agent`.

**Provas:** `AgentSettingsStore`, `ConfigLoader`.

## Ponteiros de código (provas)
* Build target: `DirectoryAnalyzer.csproj` (net48, WPF).
* Logs e dashboard: `LogService`, `DashboardService`.
* Configuração de agente: `AgentConfig`, `AgentSettingsStore`.

## Upgrade/Rollback
### Agente via MSI
* Upgrade: nova instalação substitui a anterior (WiX `MajorUpgrade`).
* Rollback: `msiexec /x DirectoryAnalyzer.Agent.msi /qn`.

**Provas:** `Installer/Product.wxs`.

## LIMITAÇÕES ATUAIS
* Mismatch de path entre instalador e resolver do agente.

## COMO VALIDAR
1. Executar WPF e verificar logs.
2. Alterar `agentclientsettings.json` e validar leitura.
3. Instalar agente e verificar `LogPath`.
