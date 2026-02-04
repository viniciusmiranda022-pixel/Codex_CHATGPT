# Arquitetura do aplicativo

## Estrutura da solução
A solução é um conjunto de projetos .NET Framework 4.8, com o aplicativo WPF como núcleo e projetos adicionais para agente, contratos e cliente.

### Pastas principais no aplicativo WPF
- `ViewModels`, estado de UI e comandos.
- `Services`, serviços de PowerShell, logs, dashboard e exportação.
- `Models`, modelos de resultado.
- `Modules`, coletores específicos, atualmente com `DnsCollector`.
- `*.xaml` e `*.xaml.cs`, views do WPF e code-behind quando aplicável.

## Padrão de módulos
### Collector
- Interface `ICollector<T>` define coleta assíncrona com cancelamento e progresso.
- Exemplo atual, `Modules/Dns/DnsCollector.cs`.

### ViewModel
- `DnsAnalyzerViewModel` concentra orquestração, status, cancelamento e bind.
- Outros módulos ainda usam code-behind e acessam `PowerShellService` diretamente.

### Services
- `PowerShellService`, execução de scripts e sanitização de parâmetros sensíveis.
- `LogService`, logs por módulo e por execução.
- `DashboardService`, persistência de `recent.json`.
- `ExportService`, exportação CSV, XML, HTML e SQL Server.

## Integração de novos módulos
Passos alinhados ao padrão existente.
1. Criar modelos em `Models` para o resultado do módulo.
2. Implementar um collector em `Modules/<Modulo>` usando `ICollector<T>`.
3. Criar um ViewModel em `ViewModels` com comandos assíncronos e cancelamento.
4. Criar o XAML e DataContext do módulo.
5. Registrar a view em `MainViewModel` no `_viewFactory`.
6. Adicionar o item de navegação em `MainWindow.xaml`.
7. Usar `LogService` e `ExportService` para logs e exportações.

## Padrões de logging e exportação
- Logs por módulo ficam em `%LocalAppData%\DirectoryAnalyzer\Logs\<ModuleName>\<ModuleName>_yyyyMMdd_HHmmss.log`.
- O dashboard grava o arquivo `%LocalAppData%\DirectoryAnalyzer\recent.json`.
- Exportações suportadas, CSV, XML, HTML e SQL Server.

## Configuração centralizada
- `PathPolicy` e `ConfigurationResolver` centralizam path, precedência e migração de configuração.
- `AgentConfigLoader` aplica defaults e overrides de registry para o agente.

## Preflight Checker, especificação
### Objetivo
Executar validações mínimas antes de cada módulo, com resultados visíveis na UI, e permitir reexecução dos checks.

### Checks obrigatórios por módulo
- DNS Analyzer: módulo PowerShell `DnsServer` e acesso ao PDC.
- GPO Analyzer: módulo PowerShell `GroupPolicy`.
- Trusts e ProxyAddresses: módulo PowerShell `ActiveDirectory`.
- IIS AppPools: módulo PowerShell `WebAdministration`.
- Módulos remotos, SMB Shares, Scheduled Tasks, Local Profiles, Installed Services, Local Security Policy e IIS AppPools: WinRM habilitado e firewall liberado no destino.
- Agente, modo agent, validação de thumbprint do cliente, thumbprint do servidor, porta HTTPS e handshake mTLS.

### Implementação na UI
- Painel de status no topo da view do módulo.
- Lista de checks com status, Sucesso, Falha e Aviso.
- Botão "Executar novamente" para refazer apenas o preflight.
- Registro em log do resultado do preflight usando `LogService`.

### Resultado esperado
- Se o preflight falhar, o botão de execução do módulo deve ficar desabilitado.
- O usuário deve ver o motivo preciso de cada falha e o comando recomendado, quando aplicável.

## Limites e decisões
- Os módulos não DNS ainda usam code-behind. A transição para MVVM é parcial e deve ser padronizada.
- Existem dois hosts de agente no repositório, `AgentService` e `DirectoryAnalyzer.Agent`, que expõem o mesmo serviço.
