# Directory Analyzer, visão geral

## Objetivo e escopo
Directory Analyzer é um aplicativo WPF para inventário read-only de Active Directory e infraestrutura associada, com suporte a execução local e execução via agente HTTPS. O objetivo é coletar informações de forma determinística, sem alterações em AD, GPO, IIS, políticas locais ou configuração de sistema. A premissa de read-only está declarada na documentação atual e guia a arquitetura e os módulos.

## Premissas
- Plataforma Windows com .NET Framework 4.8 e PowerShell.
- Coleta por módulos, com execução local via PowerShell e execução remota opcional via agente HTTPS.
- Operação read-only, sem escrita em AD ou em serviços locais.

## Componentes principais
### Aplicativo WPF
- Shell com navegação e módulos de análise.
- Módulos existentes: DNS, GPO, SMB Shares, Scheduled Tasks, Local Profiles, Service Account Analyzer, Local Security Policy, IIS AppPools, Trusts, ProxyAddresses, Dashboard, Agent Inventory e Agents.
- Painel de status e execução assíncrona por módulo.

### Serviços de suporte
- PowerShellService para execução de scripts e coleta.
- LogService para logs por módulo e por execução.
- DashboardService para registrar atividade recente em `recent.json`.
- ExportService para exportação CSV, XML, HTML e SQL Server.

### Agente on-prem
- Serviço Windows com HttpListener e HTTPS obrigatório.
- Autenticação mTLS com allowlist por thumbprint e validação de cadeia com revogação.
- Anti-replay com timestamp, nonce e correlation ID.
- Rate limiting, limite de concorrência e limite de tamanho de payload.
- Ações allow-listed no ActionRegistry.

### Cliente do agente
- Biblioteca `DirectoryAnalyzer.Agent.Client` usada pela UI.
- Console `AnalyzerClient` para testes de integração do agente.

## Fluxos principais
### Execução local
1. Usuário seleciona o módulo na UI.
2. O módulo executa coleta via PowerShellService.
3. O resultado é exibido na UI, logado em arquivo e registrado no dashboard.

### Execução via agente
1. Usuário habilita Agent Mode e configura `agentclientsettings.json`.
2. A UI cria uma requisição assinada e conecta no endpoint HTTPS do agente.
3. O agente valida certificado do cliente, anti-replay e assinatura.
4. O agente executa a ação allow-listed e retorna o resultado.

### Exportação
1. Usuário aciona exportação no módulo.
2. ExportService gera CSV, XML, HTML ou SQL Server.
3. O arquivo ou tabela gerada é persistida no destino informado pela UI.

## Caminhos e arquivos relevantes
- Configuração do agente, `%ProgramData%\DirectoryAnalyzerAgent\agentsettings.json`.
- Configuração do cliente do agente na UI, `%ProgramData%\DirectoryAnalyzerAgent\agentclientsettings.json`.
- Logs por módulo, `%LocalAppData%\DirectoryAnalyzer\Logs\<ModuleName>\<ModuleName>_yyyyMMdd_HHmmss.log`.
- Dashboard recente, `%LocalAppData%\DirectoryAnalyzer\recent.json`.
