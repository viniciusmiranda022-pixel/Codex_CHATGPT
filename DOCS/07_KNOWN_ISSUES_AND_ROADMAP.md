# Known issues e roadmap

## Known issues
1. UI do Agent Inventory expõe apenas `GetUsers`, as demais ações do agente não têm view na UI.
2. Existem dois hosts de agente no repositório, `AgentService` e `DirectoryAnalyzer.Agent`, com duplicação de lógica.
3. `TrustedCaThumbprints` é gravado pelo instalador e no registry, mas o código do agente não consome essa configuração.
4. Logs do agente não são assinados, a integridade depende de controles externos.
5. Não há RBAC no agente, o controle de acesso é apenas por allowlist de certificado.
6. Não existe Preflight Checker na UI, o diagnóstico é manual.

## Roadmap, ordem de impacto
1. Padronização de paths de configuração e precedence, consolidar `%ProgramData%\DirectoryAnalyzerAgent` e adicionar testes de regressão.
2. Correção do mismatch instalador vs resolver do agente, alinhar `AgentService/Program.cs` e `AnalyzerClient/Program.cs` para usar `%ProgramData%\\DirectoryAnalyzerAgent`, manter `Installer/Product.wxs` com `ProgramDataFolder` igual a `DirectoryAnalyzerAgent`, validar em MSI e no host do agente com testes automatizados.
3. Preflight Checker embutido no app WPF, com painel de checks e execução antes do módulo.
4. Normalização do padrão MVVM nos módulos restantes, migrar code-behind para ViewModels e collectors.
5. Opção de integridade de logs, assinatura ou HMAC de logs do agente e do WPF.
6. RBAC leve no agente para clientes regulados, definir papéis mínimos por ação.
