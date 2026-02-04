# Operações

## Operação diária
### Execução local
1. Execute os módulos pela UI e acompanhe o status no Dashboard.
2. Logs são gerados por módulo em `%LocalAppData%\DirectoryAnalyzer\Logs\<ModuleName>\<ModuleName>_yyyyMMdd_HHmmss.log`.
3. O dashboard grava a atividade recente em `%LocalAppData%\DirectoryAnalyzer\recent.json`.

### Execução via agente
1. Garanta que `%ProgramData%\DirectoryAnalyzerAgent\agentclientsettings.json` esteja configurado.
2. O módulo "Agent Inventory" executa `GetUsers` via agente (UI sempre via agente).

## Coleta de evidências
1. Logs do WPF em `%LocalAppData%\DirectoryAnalyzer\Logs\<ModuleName>`.
2. Log do agente em `%ProgramData%\DirectoryAnalyzerAgent\Logs\agent.log`.
3. Log do cliente em `%ProgramData%\DirectoryAnalyzerAgent\Logs\analyzerclient.log`.
4. Configuração do agente em `%ProgramData%\DirectoryAnalyzerAgent\agentsettings.json`.
5. Configuração do cliente do agente em `%ProgramData%\DirectoryAnalyzerAgent\agentclientsettings.json`.

## Preflight Checker, operação
Quando implementado, o preflight deve ser usado antes da execução do módulo.
1. Use o painel de status do módulo para validar módulos PowerShell, RSAT, WinRM e certificados.
2. Execute novamente o preflight após qualquer ajuste de ambiente.

TODO: O preflight não existe hoje na UI, a seção acima é uma especificação operacional que depende de implementação.

## Validação rápida pós instalação
Checklist objetivo para verificação diária.
1. Executar no host do agente `DirectoryAnalyzer.Agent.exe --doctor`.
2. Executar no host do cliente `DirectoryAnalyzer.AnalyzerClient.exe --doctor`.
3. Verificar no console e no log.
   3.1. Paths resolvidos para config e log.
   3.2. Precedência aplicada e fonte vencedora.
   3.3. Migração de config e resultado.
   3.4. Validação de escrita no diretório de log.
   3.5. Validação de JSON e campos obrigatórios.
   3.6. Validação de URL e thumbprint quando aplicável.
   3.7. Exit code 0 para sucesso e 1 para falha.

## Rotação de logs
1. O WPF gera arquivos por execução, não há rotação automática adicional.
2. O agente grava em arquivo único com append contínuo.

TODO: definir procedimento oficial de rotação de logs do agente e do WPF, não há scripts no repositório.

## Atualização de versões
1. Aplicativo WPF, substituir o executável compilado e validar execução.
2. Agente via MSI, reinstalar com versão superior e validar serviço.

## Diagnóstico sem acesso privilegiado
1. Usar logs do WPF e do agente.
2. Verificar `recent.json` para entender a última execução.
3. Coletar mensagens de erro exibidas na UI e o RequestId registrado no log.
