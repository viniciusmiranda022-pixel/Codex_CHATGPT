# Deliverable A: On-Prem Agent Architecture (Extremely Explicit)

## 1) Components (what each part is and does)

### Analyzer (WPF Desktop)
* **Role:** Primary operator UI for Directory Analyzer inventory and reporting.  
* **Responsibility:** Initiates discovery, configures targets, and sends signed/mTLS requests to agents.  
* **Runs as:** Interactive desktop app with user context.  
* **Key security behaviors:** Validates agent certs (pin/allow-list), uses client cert for mTLS, and only sends allow-listed actions.  
* **References:** The Analyzer is the central DirectoryAnalyzer application that calls agents and uses client certificate validation/pinning for mTLS. 【F:AGENT_ARCHITECTURE.md†L14-L15】【F:AGENT_ARCHITECTURE.md†L257-L280】

### Agent (Windows Service)
* **Role:** On-prem inventory executor that lives in the customer environment.  
* **Responsibility:** Exposes a hardened HTTPS endpoint; executes predefined read-only inventory actions locally; returns normalized results.  
* **Runs as:** Windows Service with a dedicated service account or gMSA, read-only AD permissions.  
* **Key security behaviors:** Enforces mTLS, verifies analyzer client certificate allow-list, validates anti-replay fields/signatures, rate limits requests, and blocks unknown actions.  
* **References:** Agent service model, request handling, action allow-list, mTLS validation, and service account hardening. 【F:AGENT_ARCHITECTURE.md†L54-L110】【F:AGENT_ARCHITECTURE.md†L149-L200】【F:AGENT_ARCHITECTURE.md†L315-L324】【F:AGENT_ARCHITECTURE.md†L420-L426】

### Contracts (Request/Response Schema)
* **Role:** Strict request/response model to keep requests bounded and auditable.  
* **Responsibility:** Defines request ID, action name, parameters, and normalized response format (status, payload, error).  
* **References:** JSON schema and examples for the request/response contract. 【F:AGENT_ARCHITECTURE.md†L218-L258】

### Client Library (Analyzer-side)
* **Role:** Encapsulates HTTP/mTLS calls to the agent, including certificate handling and request signing.  
* **Responsibility:** Builds requests (nonce, timestamp, signature), enforces TLS, pins agent certs, and parses responses.  
* **References:** C# client call with client certificate and server certificate pinning, plus signed requests. 【F:AGENT_ARCHITECTURE.md†L257-L287】

### Certificate Trust Chain (On-Prem PKI/AD CS)
* **Role:** Root of trust for mTLS between Analyzer and Agent.  
* **Responsibility:** Issues certificates to both sides; supports revocation, key protection, and lifecycle management.  
* **References:** Certificate lifecycle, issuance, import/export, and binding instructions. 【F:AGENT_ARCHITECTURE.md†L200-L217】

### Optional AD Discovery (Environment Targeting)
* **Role:** Helps locate and target agents or AD endpoints (e.g., by OU/site) without hardcoding.  
* **Responsibility:** Provides safe, read-only discovery to populate Analyzer targets and agent configuration lists.  
* **References:** Agent executes read-only queries locally under scoped service account and communicates only with the Analyzer. 【F:AGENT_ARCHITECTURE.md†L10-L18】【F:AGENT_ARCHITECTURE.md†L315-L324】

---

## 2) Data Flow (discovery → handshake → request → execution → response → logging)

1. **Discovery**  
   * Analyzer enumerates target environments (manually configured or via optional AD discovery).  
   * Output is a list of agent endpoints and expected certificate thumbprints.  
   * Agents are read-only and run locally in the AD environment. 【F:AGENT_ARCHITECTURE.md†L10-L18】【F:AGENT_ARCHITECTURE.md†L315-L324】

2. **Handshake (mTLS)**  
   * Analyzer opens HTTPS connection to the agent endpoint.  
   * Analyzer **presents its client certificate**; agent validates it against an allow-list.  
   * Analyzer **pins** the agent server certificate by thumbprint to prevent MITM.  
   * TLS is restricted to 1.2+ by policy. 【F:AGENT_ARCHITECTURE.md†L149-L200】【F:AGENT_ARCHITECTURE.md†L257-L287】

3. **Request**  
   * Analyzer builds an `AgentRequest` with `RequestId`, `ActionName`, parameters, and anti-replay fields (`TimestampUnixSeconds`, `Nonce`).  
   * Analyzer signs the canonical request with its private key.  
   * The client library POSTs JSON to the agent. 【F:AGENT_ARCHITECTURE.md†L257-L287】【F:AGENT_ARCHITECTURE.md†L472-L518】

4. **Execution**  
   * Agent validates client cert, verifies the signature, checks timestamp/nonce, and enforces rate limits.  
   * Agent uses an **action registry** to ensure only allow-listed operations run.  
   * Agent runs each action with timeouts and cancellation. 【F:AGENT_ARCHITECTURE.md†L149-L200】【F:AGENT_ARCHITECTURE.md†L260-L313】

5. **Response**  
   * Agent returns a normalized response `{Status, Payload, Error}` with timing metadata.  
   * Errors are returned as a clean error object; stack traces stay in local logs only. 【F:AGENT_ARCHITECTURE.md†L218-L258】【F:AGENT_ARCHITECTURE.md†L316-L349】

6. **Logging**  
   * Agent writes structured JSON logs with request ID, action name, client thumbprint, duration, and status.  
   * Logs are stored locally and are SIEM-friendly for audit ingestion. 【F:AGENT_ARCHITECTURE.md†L351-L383】

---

## 3) Threat Model (≥10 realistic threats + mitigations)

1. **MITM / TLS downgrade**  
   * **Threat:** Attacker intercepts or downgrades TLS.  
   * **Mitigation:** TLS 1.2+ only, certificate pinning, and mTLS with allow-listed client certs. 【F:AGENT_ARCHITECTURE.md†L149-L200】

2. **Analyzer client certificate theft**  
   * **Threat:** Stolen client cert used to call agent.  
   * **Mitigation:** Allow-list thumbprints, enforce revocation checks, and limit certs to non-exportable hardware or store-protected keys. 【F:AGENT_ARCHITECTURE.md†L149-L200】【F:AGENT_ARCHITECTURE.md†L431-L434】

3. **Agent service account compromise**  
   * **Threat:** Service account used for lateral movement.  
   * **Mitigation:** gMSA/read-only permissions, deny interactive logon, and minimal privileges. 【F:AGENT_ARCHITECTURE.md†L315-L324】【F:AGENT_ARCHITECTURE.md†L420-L426】

4. **Replay attacks**  
   * **Threat:** Captured requests are replayed.  
   * **Mitigation:** Timestamp + nonce, anti-replay cache, and request signing. 【F:AGENT_ARCHITECTURE.md†L149-L168】【F:AGENT_ARCHITECTURE.md†L431-L438】

5. **Action abuse / arbitrary command execution**  
   * **Threat:** Malicious input triggers non-approved operations.  
   * **Mitigation:** Action registry allow-list; no raw PowerShell or arbitrary commands. 【F:AGENT_ARCHITECTURE.md†L260-L313】

6. **Denial of service (request floods)**  
   * **Threat:** Attackers overload agent.  
   * **Mitigation:** Rate limiting, max concurrent requests, and request size caps. 【F:AGENT_ARCHITECTURE.md†L78-L88】【F:AGENT_ARCHITECTURE.md†L149-L168】

7. **Data exfiltration via oversized responses**  
   * **Threat:** Unbounded responses leak excessive data.  
   * **Mitigation:** Response normalization, pagination/limits, and bounded payloads. 【F:AGENT_ARCHITECTURE.md†L316-L344】

8. **Insider misuse of Analyzer UI**  
   * **Threat:** Authorized user requests more than needed.  
   * **Mitigation:** Only predefined read-only actions are exposed; all requests logged with correlation IDs. 【F:AGENT_ARCHITECTURE.md†L10-L18】【F:AGENT_ARCHITECTURE.md†L351-L383】

9. **Certificate chain spoofing**  
   * **Threat:** Attacker uses rogue cert.  
   * **Mitigation:** Organization PKI with defined templates, pinned thumbprints, and CRL/OCSP checks. 【F:AGENT_ARCHITECTURE.md†L200-L217】【F:AGENT_ARCHITECTURE.md†L431-L434】

10. **Local log tampering**  
   * **Threat:** Attacker alters logs to hide actions.  
   * **Mitigation:** JSON logs with centralized collection (SIEM), file ACLs, and rotation. 【F:AGENT_ARCHITECTURE.md†L351-L383】

11. **Malicious configuration changes**  
   * **Threat:** Changes to agent bind port or allowed certs.  
   * **Mitigation:** Config stored in protected locations; MSI/registry policy controls; least-privileged service account. 【F:AGENT_ARCHITECTURE.md†L92-L104】【F:AGENT_ARCHITECTURE.md†L393-L414】

12. **Weak cipher usage**  
   * **Threat:** Weak ciphers allow traffic decryption.  
   * **Mitigation:** Schannel hardening, disable RC4, enforce TLS 1.2+. 【F:AGENT_ARCHITECTURE.md†L181-L200】【F:AGENT_ARCHITECTURE.md†L399-L413】

---

## 4) Why TLS/mTLS On-Prem Is Secure and Standard

* **TLS is the de-facto standard for secure service-to-service communication**: it provides encryption in transit, integrity, and server authentication, with mature OS support (Schannel) and enterprise policy enforcement. The agent design uses Windows’ HTTPS stack, which is enterprise-hardenable via GPO and registry policies. 【F:AGENT_ARCHITECTURE.md†L149-L200】
* **mTLS adds mutual identity assurance**: both the Analyzer and Agent present certificates, which are validated and pinned to an allow-list, blocking unauthorized clients even if they know the endpoint. This is a common enterprise pattern for on-prem service identity. 【F:AGENT_ARCHITECTURE.md†L149-L200】【F:AGENT_ARCHITECTURE.md†L257-L287】
* **PKI-backed trust chain is already standard in on-prem environments**: AD CS or enterprise PKI provides issuance, revocation, and lifecycle governance for service certificates. This is normal practice for internal services with security requirements. 【F:AGENT_ARCHITECTURE.md†L200-L217】
* **Policy-controlled cipher suites and revocation checks are enforceable**: TLS versions and ciphers can be hardened at the OS level, and certificate revocation can be checked online. This gives predictable compliance behavior. 【F:AGENT_ARCHITECTURE.md†L181-L200】【F:AGENT_ARCHITECTURE.md†L431-L434】
* **Attack surface is minimized compared to legacy remoting**: mTLS with a single inbound port and no arbitrary execution is safer than broad remote management endpoints. The agent only exposes approved actions. 【F:AGENT_ARCHITECTURE.md†L10-L18】【F:AGENT_ARCHITECTURE.md†L260-L313】
