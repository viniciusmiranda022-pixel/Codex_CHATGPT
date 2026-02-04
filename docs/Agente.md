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
