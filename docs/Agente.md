# Agente (DirectoryAnalyzer.Agent)

## Finalidade
Documentar o agente HTTPS do DirectoryAnalyzer, com foco em autenticação mTLS, assinatura de requisições, anti-replay e rate limiting, **exatamente como implementado**.

## Público-alvo
* Segurança
* Infra/AD
* Operações
* Desenvolvimento

## Premissas
* O agente roda em Windows como console ou serviço.
* HTTPS é obrigatório.
* O agente usa `HttpListener` e autenticação por certificado.

## Por que o agente existe
O agente viabiliza coleta remota em ambientes onde a estação do operador não tem acesso direto a AD/servidores. Ele expõe um conjunto **mínimo e allow-listed** de ações de inventário.

**Provas:** `AgentHost`, `ActionRegistry`.

## Endpoints e payloads
### Endpoint único
* **Path:** `/agent/`
* **Método:** `POST`
* **Content-Type:** `application/json`

**Provas:** `AgentHost.HandleRequestAsync` valida método e content-type.

### Exemplo de request (GetUsers)
```json
{
  "RequestId": "<guid>",
  "ActionName": "GetUsers",
  "Parameters": {
    "IncludeDisabled": "false"
  },
  "TimestampUnixSeconds": 1700000000,
  "Nonce": "<nonce>",
  "Signature": "<base64>",
  "CorrelationId": "<guid>"
}
```

### Exemplo de response (sucesso)
```json
{
  "RequestId": "<guid>",
  "Status": 0,
  "DurationMs": 120,
  "Payload": {
    "Users": [
      {
        "SamAccountName": "jdoe",
        "DisplayName": "Jane Doe",
        "Enabled": true,
        "DistinguishedName": "CN=Jane Doe,OU=Users,DC=contoso,DC=local",
        "UserPrincipalName": "jdoe@contoso.local",
        "ObjectSid": "S-1-5-21-..."
      }
    ]
  }
}
```

**Provas:** `DirectoryAnalyzer.Agent.Contracts/AgentContracts.cs`.

## Autenticação e autorização
### mTLS (certificado do cliente)
* O agente exige conexão segura (`HTTPS`).
* O certificado do cliente deve estar em allowlist (`AnalyzerClientThumbprints`).
* Validação de cadeia e revogação é configurável.

**Provas:** `AgentHost.ValidateClientCertificateAsync`, `AgentConfig`.

### Assinatura de requisição
* O cliente assina o payload com o certificado.
* O agente valida a assinatura com a chave pública.

**Provas:** `AgentRequestSigner.Sign/VerifySignature`.

## Anti-replay
* Exige `TimestampUnixSeconds`, `Nonce` e `CorrelationId`.
* Rejeita fora da janela `RequestClockSkewSeconds`.
* Bloqueia nonce duplicado por `ReplayCacheMinutes`.

**Provas:** `AgentHost.ValidateAntiReplay`, `NonceCache`.

## Rate limiting e concorrência
* Sliding window por thumbprint.
* Burst limit com backoff.
* Limite de concorrência via `SemaphoreSlim`.

**Provas:** `SlidingWindowRateLimiter`, `AgentConfig`.

## Action allow-list (ações implementadas)
| Ação | Fonte | Saída |
| --- | --- | --- |
| GetUsers | `ActionRegistry.GetUsersAsync` | `GetUsersResult` |
| GetGroups | `ActionRegistry.GetGroupsAsync` | `GetGroupsResult` |
| GetComputers | `ActionRegistry.GetComputersAsync` | `GetComputersResult` |
| GetGpos | `ActionRegistry.GetGposAsync` | `GetGposResult` |
| GetDnsZones | `ActionRegistry.GetDnsZonesAsync` | `GetDnsZonesResult` |

**Provas:** `ActionRegistry`, `AgentContracts.cs`.

### Catálogo e gate do agente conectado ao Broker (issue #48)
No fluxo Broker → agente (`BrokerAgentWorker`), todo `JobRequest` passa por `ModuleRequestGate`
(`src/DirectoryAnalyzer.Agent/Modules/`) **antes** de qualquer execução. Somente os módulos do
`ModuleCatalog` padrão acima são aceitos, cada um com seus parâmetros declarados:

| Módulo | Escopo imposto pelo agente | Parâmetros aceitos |
| --- | --- | --- |
| GetUsers, GetGroups, GetComputers | `defaultNamingContext` do domínio do agente (`AgentConfig.Domain` ou RootDSE local) | GetUsers: `IncludeDisabled` (true/false); demais: nenhum |
| GetGpos | `CN=Policies,CN=System,<domínio>` | nenhum |
| GetDnsZones | somente `CN=MicrosoftDNS,DC=DomainDnsZones,<domínio>` (ForestDnsZones nunca é consultado) | nenhum |

O filtro LDAP livre (`LdapFilter`) saiu do contrato remoto: a expressão de consulta é fixa no agente e o
pedido que o envia é recusado com `UnknownParameter`. As bases por módulo estão em `DirectorySearchBases`.

Recusas voltam à WPF como `ModuleResult` sem itens, com `ErrorInfo.ExceptionType = "AgentGate"` e um dos códigos:
`ArbitraryScriptRejected` (`RunPowerShellScript` foi removida), `ModuleNotLocal` (módulos PowerShell com
fan-out remoto, pendentes de decisão), `UnknownModule`, `InvalidModuleName`, `UnknownParameter`,
`InvalidParameter`, `MissingParameter`, `TargetMismatch` (job sem este agente como alvo), `AmbiguousTarget`
(lista com mais de um alvo) e `UnsupportedContract`.

O Broker exige exatamente um `TargetAgentIds` (400 `InvalidTarget`), recusa o despacho quando o agente não está
conectado (409 `AgentUnavailable`) ou quando há mais de uma conexão com o mesmo AgentId (409 `AmbiguousTarget`,
nada é despachado). O job fica vinculado à conexão que recebeu o despacho: somente ela pode enviar
`ProgressUpdate`/`SubmitResult`. Se essa conexão cair antes do resultado, o job vai para `Failed`
(`AgentDisconnected`). O job também vai para `Failed` quando o resultado só contém erros.

Na WPF, `BrokerJobService.ExecuteAsync` devolve um `CollectionOutcome` que distingue coleta concluída, concluída
sem itens, parcial, recusada (Broker ou agente), agente indisponível e módulo indisponível nesta versão; o painel
registra o mesmo estado. As 10 telas que ainda montam script PowerShell avisam ao abrir e ao clicar, sem enviar nada
(`ModuleAvailability`).
Na WPF, o agente alvo é configurado em **Agentes → Agente alvo (AgentId)** (`BrokerClientSettings.TargetAgentId`).

Testes: `dotnet test src/DirectoryAnalyzer.Agent.Tests` (net8.0, dados sintéticos, Broker em memória).

## Configuração do agente
### Arquivo `agentsettings.json`
* Local padrão: `%ProgramData%\DirectoryAnalyzerAgent\agentsettings.json`.
* `BindPrefix` (default: `https://+:8443/agent/`)
* `CertThumbprint`
* `AnalyzerClientThumbprints` (allowlist)
* `ActionTimeoutSeconds` (default 30)
* `LogPath` (default `%ProgramData%\DirectoryAnalyzerAgent\Logs\agent.log`)
* `Domain` (se definido, força LDAP no domínio)
* `MaxRequestBytes` (default 65536)
* `RequestClockSkewSeconds` (default 300)
* `ReplayCacheMinutes` (default 10)
* `RequireSignedRequests` (default true)
* `MaxRequestsPerMinute` (default 60)
* `MaxConcurrentRequests` (default 10)
* `EnforceRevocationCheck` (default true)
* `FailOpenOnRevocation` (default false)

**Provas:** `AgentConfig`.

### Registry overrides
* `HKLM\SOFTWARE\DirectoryAnalyzer\Agent` (campos equivalentes).

**Provas:** `AgentConfigLoader`.

## Instalação (MSI/WiX)
* Instalador WiX: `Installer/Product.wxs`.
* Parâmetros (exemplos): `LISTENPORT`, `CERTTHUMBPRINT`, `ALLOWEDCLIENTCERTTHUMBPRINTS`, `CREATEFIREWALLRULE`.

**Provas:** `Installer/README.md`, `Product.wxs`.

## Regras de firewall
* Porta padrão: **8443/TCP**.
* O instalador **pode criar** regra se `CREATEFIREWALLRULE=1`.

**Provas:** `Product.wxs` (CustomAction AddFirewallRule).

## Teste ponta-a-ponta
### Opção 1: AnalyzerClient
1. Configurar `agentclientsettings.json`.
2. Executar `AnalyzerClient` e validar resposta.

**Provas:** `AnalyzerClient/Program.cs`.

### Opção 2: UI WPF
1. Abrir “Agent Inventory”.
2. Executar consulta de usuários (fluxo sempre via agente).

**Provas:** `AgentInventoryViewModel`.

## Ponteiros de código (provas)
* Host e pipeline HTTP: `AgentHost`.
* Allowlist e ações: `ActionRegistry`.
* Contratos e assinatura: `AgentContracts`.
* Cliente do agente: `DirectoryAnalyzer.Agent.Client/AgentClient.cs`.

## LIMITAÇÕES ATUAIS
* UI só expõe `GetUsers` (não há views para outras ações do agente).
* Logs do agente são texto JSON sem assinatura (integridade operacional depende do SO).

## COMO VALIDAR
1. Instalar o agente com MSI e configurar certificados.
2. Validar porta com `Test-NetConnection`.
3. Enviar request do `AnalyzerClient`.
4. Verificar log em `LogPath`.
