# Operações

## Operação diária
### Execução local
- Execute os módulos pela UI e acompanhe o status no Dashboard.
- Logs são gerados por módulo em `%LocalAppData%\DirectoryAnalyzer\Logs\<ModuleName>\<ModuleName>_yyyyMMdd_HHmmss.log`.
- O dashboard grava a atividade recente em `%LocalAppData%\DirectoryAnalyzer\recent.json`.

### Execução via agente
- Habilite Agent Mode na UI.
- Garanta que `%ProgramData%\DirectoryAnalyzerAgent\agentclientsettings.json` esteja configurado.
- O módulo "Agent Inventory" executa `GetUsers` via agente.

## Coleta de evidências
- Logs do WPF, `%LocalAppData%\DirectoryAnalyzer\Logs\<ModuleName>`.
- Log do agente, `%ProgramData%\DirectoryAnalyzerAgent\Logs\agent.log`.
- Log do cliente, `%ProgramData%\DirectoryAnalyzerAgent\Logs\analyzerclient.log`.
- Configuração do agente, `%ProgramData%\DirectoryAnalyzerAgent\agentsettings.json`.
- Configuração do cliente do agente, `%ProgramData%\DirectoryAnalyzerAgent\agentclientsettings.json`.

## Preflight Checker, operação
Quando implementado, o preflight deve ser usado antes da execução do módulo.
- Use o painel de status do módulo para validar módulos PowerShell, RSAT, WinRM e certificados.
- Execute novamente o preflight após qualquer ajuste de ambiente.

TODO: O preflight não existe hoje na UI, a seção acima é uma especificação operacional que depende de implementação.

## Validação rápida pós-instalação
Checklist objetivo para verificação diária.
1. Executar no host do agente: `DirectoryAnalyzer.Agent.exe --doctor`.
2. Executar no host do cliente: `DirectoryAnalyzer.AnalyzerClient.exe --doctor`.
3. Verificar no console e no log:
   - paths resolvidos para config e log.
   - precedência aplicada e fonte vencedora.
   - migração de config e resultado.
   - validação de escrita no diretório de log.
   - validação de JSON e campos obrigatórios.

## Rotação de logs
- O WPF gera arquivos por execução, não há rotação automática adicional.
- O agente grava em arquivo único com append contínuo.

TODO: definir procedimento oficial de rotação de logs do agente e do WPF, não há scripts no repositório.

## Atualização de versões
- Aplicativo WPF, substituir o executável compilado e validar execução.
- Agente via MSI, reinstalar com versão superior e validar serviço.

## Diagnóstico sem acesso privilegiado
- Usar logs do WPF e do agente.
- Verificar `recent.json` para entender a última execução.
- Coletar mensagens de erro exibidas na UI e o RequestId registrado no log.
