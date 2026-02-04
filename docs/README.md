# DirectoryAnalyzer — Documentação Oficial (PT-BR)

## Finalidade
Este documento fornece a visão executiva e o ponto de entrada para o uso do DirectoryAnalyzer, descrevendo **o que existe de fato no repositório**, como compilar e executar e onde validar cada componente. Toda afirmação relevante aponta para código-fonte existente.

## Público-alvo
* Segurança (GRC, blue team, risk assessment)
* Infraestrutura Windows / AD
* Times de Operações e Suporte
* Desenvolvimento / Engenharia
* Pré-vendas (com foco técnico)

## Premissas
* O repositório utiliza **.NET Framework 4.8** em projetos principais e mantém operação **read-only**.
* A execução ocorre em ambiente Windows com PowerShell e módulos necessários instalados.

## Visão executiva (o que é e para que serve)
O DirectoryAnalyzer é uma aplicação WPF para inventário **somente leitura** de Active Directory e infraestrutura associada (DNS, GPO, compartilhamentos SMB, tarefas agendadas, perfis locais, política de segurança local, IIS AppPools, trusts, proxyAddresses). Também inclui um **agente opcional** para coleta remota via HTTPS com mTLS e assinatura de requisições.

## Ponteiros de código (provas)
* Aplicação WPF e navegação de módulos: `MainWindow.xaml`, `MainViewModel`.
* Coleta via PowerShell: `PowerShellService`.
* Exportação: `ExportService`.
* Agente: `AgentHost`, `ActionRegistry`, `AgentContracts`.

## Quick start (build + run)
### Visual Studio
1. Abrir `DirectoryAnalyzer.sln`.
2. Restaurar pacotes NuGet.
3. Compilar em Debug/Release.
4. Executar o projeto `DirectoryAnalyzer`.

### Linha de comando (PowerShell)
```powershell
msbuild .\DirectoryAnalyzer.sln /t:Restore,Build /p:Configuration=Debug
.\bin\Debug\net48\DirectoryAnalyzer.exe
```

## Features mapeadas ao que existe no `.sln`
* **WPF app** (DirectoryAnalyzer): interface com navegação de módulos e status global.
* **Coleta DNS** (DNS Analyzer): zonas, registros e encaminhadores.
* **Coleta GPO** (GPO Analyzer): resumo, links, delegação e filtros.
* **Coleta SMB** (SMB Shares Analyzer): shares e ACLs.
* **Coleta de tarefas agendadas** (Scheduled Tasks Analyzer).
* **Coleta de perfis locais** (Local Profiles Analyzer).
* **Coleta de política de segurança local** (Local Security Policy Analyzer).
* **Coleta de IIS AppPools** (IIS AppPools Analyzer).
* **Coleta de trusts** (Trusts Analyzer).
* **Coleta de ProxyAddresses** (ProxyAddresses Analyzer).
* **Agente opcional + cliente** (DirectoryAnalyzer.Agent + Agent.Client + AnalyzerClient).

**Provas (ponteiros de código):**
* Mapeamento de views e navegação: `MainWindow.xaml`, `MainViewModel`.
* Implementações de módulos: `DnsAnalyzerViewModel`, `GpoAnalyzerView.xaml.cs`, `SmbAnalyzerView.xaml.cs`, `ScheduledTasksAnalyzerView.xaml.cs`, `LocalProfilesAnalyzerView.xaml.cs`, `LocalSecurityPolicyAnalyzerView.xaml.cs`, `IisAnalyzerView.xaml.cs`, `TrustAnalyzerView.xaml.cs`, `ProxyAddressAnalyzerView.xaml.cs`.
* Agente e contratos: `AgentHost`, `ActionRegistry`, `AgentContracts`.

## LIMITAÇÕES ATUAIS
* **Desalinhamento de path de configuração do agente:** o instalador grava `agentsettings.json` em `%ProgramData%\DirectoryAnalyzerAgent`, enquanto o agente padrão busca em `%ProgramData%\DirectoryAnalyzer`. (Ver `Installer/Product.wxs` e `AgentHost/Program.cs` nos projetos de agente.)
* **UI do Agent Inventory expõe apenas `GetUsers`:** as demais ações do agente não estão ligadas a views na UI.

## COMO VALIDAR
1. **Compilar** a solução (Visual Studio ou `msbuild`).
2. **Executar** o WPF (`DirectoryAnalyzer.exe`).
3. **Rodar um módulo** (ex.: DNS Analyzer) e verificar log gerado em `%LocalAppData%\DirectoryAnalyzer\Logs\DNS Analyzer\`.
4. **Validar exportações** (CSV/XML/HTML/SQL) usando o botão de exportação do módulo.
5. **Validar agente (opcional):** executar `DirectoryAnalyzer.Agent.exe` em modo console e testar via `AnalyzerClient`.
