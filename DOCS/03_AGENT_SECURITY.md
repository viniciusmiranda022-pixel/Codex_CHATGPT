# Segurança do agente

## Modelo de ameaça, resumo e mitigação
A lista abaixo descreve ameaças observadas e a mitigação existente no código ou operacional.

1. MITM e downgrade de TLS
   - Mitigação no código, HTTPS obrigatório e validação de certificado no cliente e no agente.

2. Cliente não autorizado chamando o agente
   - Mitigação no código, allowlist por thumbprint em `AnalyzerClientThumbprints`.

3. Replay de requisições
   - Mitigação no código, `TimestampUnixSeconds`, `Nonce` e `CorrelationId` com cache anti-replay.

4. Tampering do request
   - Mitigação no código, assinatura da requisição pelo cliente, verificação no agente.

5. Flood e abuso de requisições
   - Mitigação no código, rate limiting por thumbprint com backoff e `Retry-After`.

6. Excesso de concorrência
   - Mitigação no código, `MaxConcurrentRequests` via `SemaphoreSlim`.

7. Payload excessivo
   - Mitigação no código, limite por `MaxRequestBytes`.

8. Certificado do agente inválido ou spoofing
   - Mitigação no código, validação de cadeia e hostname do servidor no cliente.

9. Certificado do cliente inválido
   - Mitigação no código, validação de cadeia e revogação no agente.

10. Abuso de ações não permitidas
    - Mitigação no código, ActionRegistry com allowlist estrita de ações.

## Controles de segurança implementados
- mTLS obrigatório com certificados X.509.
- Allowlist por thumbprint para cliente e validação de cadeia com revogação.
- Assinatura de request e verificação no agente.
- Anti-replay com nonce, timestamp e cache.
- Rate limiting e limite de concorrência.
- Limite de tamanho do request e verificação de Content-Type.
- Logs estruturados em JSON com RequestId, ação e status.

## O que não existe e implicações
- Logs não são assinados, a integridade depende de controles do sistema operacional e coleta externa.
- Não há RBAC no agente, o controle de acesso é apenas por certificado allow-listed.
- Não há allowlist de CA no código, mesmo que o instalador aceite `TrustedCaThumbprints`.

## Boas práticas operacionais
- Rotação de certificados do agente e do cliente, com revogação habilitada.
- Chaves privadas armazenadas em store do Windows, não exportáveis quando possível.
- Permissões restritas no arquivo de configuração e no arquivo de log do agente.
- Executar o serviço do agente com conta dedicada e permissões read-only no AD.

## TODOs por falta de evidência
- TODO: definir e documentar procedimento oficial de rotação de logs do agente, não há implementação no código.
- TODO: especificar processo de assinatura ou HMAC de logs, não existe no repositório.
