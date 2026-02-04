# Segurança

## Finalidade
Fornecer documentação de segurança para avaliação formal do DirectoryAnalyzer, incluindo ameaças, mitigações no código e controles operacionais.

## Público-alvo
* Segurança / GRC
* Infraestrutura
* Engenharia

## Premissas
* O software opera em **read-only**.
* TLS/mTLS é suportado pelo Windows (sem custos de licença).
* Certificados podem ser CA interna ou pública.

## Threat model (>= 12 ameaças)
> Cada ameaça indica se a mitigação está **no código** ou é **operacional**.

1) **MITM no canal do agente**
* Mitigação (código): HTTPS obrigatório + validação de certificado.
* Provas: `AgentHost` e `AgentClient`.

2) **Cliente não autorizado acessa o agente**
* Mitigação (código): allowlist por thumbprint.
* Provas: `AgentConfig.AnalyzerClientThumbprints`, `ValidateClientCertificateAsync`.

3) **Replay de requisição**
* Mitigação (código): nonce + timestamp + cache anti-replay.
* Provas: `ValidateAntiReplay`, `NonceCache`.

4) **Tampering de request**
* Mitigação (código): assinatura com certificado do cliente.
* Provas: `AgentRequestSigner`.

5) **Flood/DoS no agente**
* Mitigação (código): rate limit + burst + backoff.
* Provas: `SlidingWindowRateLimiter`.

6) **Excesso de carga por concorrência**
* Mitigação (código): `MaxConcurrentRequests` com `SemaphoreSlim`.
* Provas: `AgentHost`.

7) **Payload excessivo**
* Mitigação (código): `MaxRequestBytes`.
* Provas: `AgentHost`.

8) **LDAP injection nos filtros**
* Mitigação (código): validação estrita de `LdapFilter`.
* Provas: `ActionRegistry.IsLdapFilterSafe`.

9) **Spoofing do servidor (certificado inválido)**
* Mitigação (código): validação de cadeia e hostname/SAN.
* Provas: `AgentClient.ValidateServerCertificate`.

10) **Vazamento de credenciais em logs**
* Mitigação (código): sanitização de parâmetros sensíveis.
* Provas: `PowerShellService.SanitizeParameters`.

11) **Integridade de logs**
* Mitigação: **operacional** (ACLs/backup).
* NÃO VERIFICADO no código: não há assinatura de log.

12) **Escalada de privilégio no agente**
* Mitigação: **operacional** (conta read-only/gMSA).
* NÃO VERIFICADO no código: não há enforcement de RBAC.

## Classificação dos dados coletados
* **Identidade**: usuários, grupos, computadores, GPOs.
* **Infraestrutura**: DNS, SMB, IIS, tarefas, políticas locais.

**Observação:** dados devem ser tratados como **sensíveis** pela equipe de segurança.

## Hardening (passos concretos)
1. Usar conta dedicada read-only para o agente.
2. Habilitar revogação de certificados (CRL/OCSP) sempre que possível.
3. Fixar thumbprint do servidor no cliente quando requerido.
4. Restringir `BindPrefix` a interfaces internas.
5. Limitar acesso a logs com ACLs.

## Criptografia
* **TLS não é “pago”**: é protocolo suportado pelo Windows.
* **Certificados podem ser CA interna ou pública**.
* **RC4 NÃO aumenta segurança** e **não deve ser usado**.

## O que é mitigado no código vs operacional
* **Código:** mTLS, assinatura, anti-replay, rate limiting.
* **Operacional:** hardening de contas, ACLs, backup, SIEM.

## Ponteiros de código (provas)
* Pipeline HTTPS do agente: `AgentHost`.
* Assinatura: `AgentRequestSigner`.
* Anti-replay: `NonceCache`, `ValidateAntiReplay`.
* Rate limit: `SlidingWindowRateLimiter`.

## LIMITAÇÕES ATUAIS
* Logs não são assinados.
* Não há RBAC no agente (apenas allowlist).

## COMO VALIDAR
1. Verificar allowlist e certificados configurados.
2. Executar requisição assinada e validar resposta.
3. Testar replay (mesmo nonce) e validar erro.
4. Testar rate limit (alta frequência) e validar HTTP 429.
