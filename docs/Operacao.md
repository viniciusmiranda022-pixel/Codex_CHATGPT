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

## Execução: logs
* Logs por módulo: `%LocalAppData%\DirectoryAnalyzer\Logs\<ModuleName>\<ModuleName>_yyyyMMdd_HHmmss.log`.
* Dashboard: `%LocalAppData%\DirectoryAnalyzer\recent.json`.

**Provas:** `LogService`, `DashboardService`.

## Execução: configurações
### Agent Mode (UI)
* `agentclientsettings.json` em `%ProgramData%\DirectoryAnalyzerAgent\` (preferencial) ou base dir.

### Agente (service/console)
* `agentsettings.json` em `%ProgramData%\DirectoryAnalyzerAgent\` (preferencial) ou base dir.
* Overrides no registry: `HKLM\SOFTWARE\DirectoryAnalyzer\Agent`.

### Precedência
1. Se o arquivo existir em `%ProgramData%\DirectoryAnalyzerAgent\`, ele é usado.
2. Se não existir no path novo e existir no path legado `%ProgramData%\DirectoryAnalyzer\`, o arquivo é copiado para o path novo e o novo é usado.
3. Se não existir no ProgramData, o arquivo na base do executável é usado.
4. Se o JSON do agente não existir, ele é criado com valores do registry e defaults.
5. Para o agente, valores no registry sobrescrevem o JSON quando preenchidos.

**Provas:** `AgentSettingsStore`, `AgentConfigLoader`.

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
* `TrustedCaThumbprints` não é consumido pelo agente, o JSON e o registry podem conter o valor, mas o host não aplica allowlist de CA.

## COMO VALIDAR
1. Executar WPF e verificar logs.
2. Alterar `agentclientsettings.json` e validar leitura.
3. Instalar agente e verificar `LogPath`.
