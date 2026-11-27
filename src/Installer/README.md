# DirectoryAnalyzer Agent MSI (WiX)

## Documentação consolidada
Use `DOCS/01_OVERVIEW.md` como ponto de entrada da documentação atual.

## O que o pacote instala

A partir da versão **1.1.0.0**, o MSI (x64) empacota `src/DirectoryAnalyzer.Agent`: o serviço
`DirectoryAnalyzerAgent` **abre uma conexão HTTPS de saída** com o Broker (SignalR) e executa apenas o catálogo
local de módulos de inventário de leitura. Não há listener nem regra de firewall de entrada.

A versão 1.0.0.0 empacotava `src/AgentService`, que abria um listener HTTPS de entrada (`https://+:8443/agent/`)
e não se conectava ao Broker. Os dois geram `DirectoryAnalyzer.Agent.exe`; a atualização substitui um pelo outro.

## Instalação silenciosa

```powershell
msiexec /i DirectoryAnalyzer.Agent.msi /qn BROKERURL="https://broker.example/agent-hub" AGENTID="origem-srv01" BROKERCLIENTCERTTHUMBPRINT="<THUMBPRINT_CERT_CLIENTE>" /l*v install.log
```

| Propriedade | Obrigatória | Default | Uso |
|---|---|---|---|
| `BROKERURL` | sim | — | URL `https://` do hub do Broker. A instalação para sem ela. |
| `AGENTID` | não | nome do computador | Identificador do agente usado pelo Broker como alvo. |
| `BROKERCLIENTCERTTHUMBPRINT` | não* | — | Certificado de cliente (LocalMachine\My) apresentado ao Broker. *Obrigatório quando o Broker exigir mTLS. |
| `DOMAIN` | não | domínio do computador | Domínio cujo RootDSE o agente lê. |
| `SERVICEACCOUNT` / `SERVICEPASSWORD` | não | `NT AUTHORITY\NetworkService` | Conta do serviço. NetworkService lê o AD como a conta de computador. |
| `ACTIONTIMEOUTSECONDS` | não | 30 | Tempo máximo por módulo. |
| `LOGPATH` | não | `C:\ProgramData\DirectoryAnalyzerAgent\Logs\agent.log` | Log do agente. |

Em atualizações, `BROKERURL`, `AGENTID` e `BROKERCLIENTCERTTHUMBPRINT` já gravados no registro são reaproveitados
quando não forem informados de novo.

Desinstalação:

```powershell
msiexec /x DirectoryAnalyzer.Agent.msi /qn /l*v uninstall.log
```

## Configuração gravada

* `HKLM\SOFTWARE\DirectoryAnalyzer\Agent` (visão de 64 bits): `BrokerUrl`, `AgentId`,
  `BrokerClientCertificateThumbprint`, `Domain`, `ActionTimeoutSeconds`, `LogPath`. Esses valores têm precedência
  sobre `%ProgramData%\DirectoryAnalyzerAgent\agentsettings.json`, que o serviço cria se não existir.
* `%ProgramData%\DirectoryAnalyzerAgent` e `Logs` recebem ACL própria: SYSTEM e Administradores (controle total) e
  NetworkService (leitura/escrita). Com `SERVICEACCOUNT` diferente, conceda essas permissões à conta escolhida.
* O pacote não executa PowerShell nem interpola propriedades em scripts.

## Atualização a partir da 1.0.0.0

* A regra de entrada `DirectoryAnalyzer Agent` (criada com `CREATEFIREWALLRULE=1`) é removida pelo nome.
* A reserva de URL e o binding TLS do listener antigo, se tiverem sido criados manualmente, devem ser removidos
  à parte: `netsh http delete urlacl url=https://+:8443/agent/` e `netsh http delete sslcert ipport=0.0.0.0:8443`.
* Valores antigos em `HKLM\SOFTWARE\WOW6432Node\DirectoryAnalyzer\Agent` (MSI x86) não são lidos pelo agente.

## Validação automatizada (sem Windows)

`dotnet test src/DirectoryAnalyzer.Agent.Tests --filter InstallerPackagingTests` confere o projeto referenciado, a
lista de arquivos contra a saída real do build net48, a ausência de listener no executável e de regra de entrada no
pacote. A compilação do MSI e a instalação exigem Windows com WiX Toolset 3.11+.
