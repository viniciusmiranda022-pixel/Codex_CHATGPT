# Directory Analyzer, visão geral

## Objetivo e escopo
Directory Analyzer é um aplicativo WPF para inventário read only de Active Directory e infraestrutura associada, com suporte a execução local e execução via agente HTTPS. O objetivo é coletar informações de forma determinística, sem alterações em AD, GPO, IIS, políticas locais ou configuração de sistema. A premissa de read only está declarada na documentação atual e guia a arquitetura e os módulos.

## Premissas
1. Plataforma Windows com .NET Framework 4.8 e PowerShell.
2. Coleta por módulos, com execução local via PowerShell e execução remota opcional via agente HTTPS.
3. Operação read only, sem escrita em AD ou em serviços locais.

## Componentes principais
### Aplicativo WPF
1. Shell com navegação e módulos de análise.
2. Módulos existentes: DNS, GPO, SMB Shares, Scheduled Tasks, Local Profiles, Service Account Analyzer, Local Security Policy, IIS AppPools, Trusts, ProxyAddresses, Dashboard, Agent Inventory e Agents.
3. Painel de status e execução assíncrona por módulo.

### Serviços de suporte
1. PowerShellService para execução de scripts e coleta.
2. LogService para logs por módulo e por execução.
3. DashboardService para registrar atividade recente em `recent.json`.
4. ExportService para exportação CSV, XML, HTML e SQL Server.

### Agente on prem
1. Serviço Windows com HttpListener e HTTPS obrigatório.
2. Autenticação mTLS com allow list por thumbprint e validação de cadeia com revogação.
3. Anti replay com timestamp, nonce e correlation ID.
4. Rate limiting, limite de concorrência e limite de tamanho de payload.
5. Ações allow list no ActionRegistry.

### Cliente do agente
1. Biblioteca `DirectoryAnalyzer.Agent.Client` usada pela UI.
2. Console `AnalyzerClient` para testes de integração do agente.

## Fluxos principais
### Execução local
1. Usuário seleciona o módulo na UI.
2. O módulo executa coleta via PowerShellService.
3. O resultado é exibido na UI, logado em arquivo e registrado no dashboard.

### Execução via agente
1. Usuário configura `agentclientsettings.json` (UI sempre via agente).
2. A UI cria uma requisição assinada e conecta no endpoint HTTPS do agente.
3. O agente valida certificado do cliente, anti replay e assinatura.
4. O agente executa a ação allow list e retorna o resultado.

### Exportação
1. Usuário aciona exportação no módulo.
2. ExportService gera CSV, XML, HTML ou SQL Server.
3. O arquivo ou tabela gerada é persistida no destino informado pela UI.

## Caminhos e arquivos relevantes
1. Configuração do agente em `%ProgramData%\DirectoryAnalyzerAgent\agentsettings.json`.
2. Configuração do cliente do agente na UI em `%ProgramData%\DirectoryAnalyzerAgent\agentclientsettings.json`.
3. Logs por módulo em `%LocalAppData%\DirectoryAnalyzer\Logs\<ModuleName>\<ModuleName>_yyyyMMdd_HHmmss.log`.
4. Dashboard recente em `%LocalAppData%\DirectoryAnalyzer\recent.json`.

## Documentação complementar
1. Problemas conhecidos e roadmap em `DOCS/07_KNOWN_ISSUES_AND_ROADMAP.md`.
