# Directory Analyzer (Agent-Only, Read-Only AD Inventory)

Directory Analyzer é uma aplicação WPF para inventário **read-only** de Active Directory e infraestrutura associada. A arquitetura é **agent-only** e todo tráfego passa pelo **Broker** via **HTTPS (TLS 1.2+)**.

## Arquitetura (obrigatória)

```
UI (WPF)  --->  Broker (HTTPS + mTLS opcional)  --->  Agents (SignalR HTTPS)
```

- **UI** nunca coleta diretamente (sem WinRM/Invoke-Command/CIM/Get-AD* na UI).
- **Broker** gerencia jobs, progresso e resultados.
- **Agents** executam coletas locais e devolvem `ModuleResult` via contratos únicos.
- **Read-only** garantido em toda a cadeia.

## Estrutura do repositório

- `src/DirectoryAnalyzer.UI` — UI WPF
- `src/DirectoryAnalyzer.Broker` — ASP.NET Core (Broker)
- `src/DirectoryAnalyzer.Agent` — Agent (Windows Service/Console)
- `src/DirectoryAnalyzer.Contracts` — DTOs compartilhados
- `src/DirectoryAnalyzer.Collectors` — coletores (PowerShell)
- `docs/` — documentação

## Como rodar (ordem)

### 1) Broker
```powershell
# Broker ASP.NET Core
 dotnet run --project src/DirectoryAnalyzer.Broker/DirectoryAnalyzer.Broker.csproj
```

### 2) Agent
```powershell
# Agent (console)
 dotnet run --project src/DirectoryAnalyzer.Agent/DirectoryAnalyzer.Agent.csproj
```

### 3) UI
```powershell
# Build
 msbuild .\DirectoryAnalyzer.sln /t:Restore,Build /p:Configuration=Debug

# Executar
 .\src\DirectoryAnalyzer.UI\bin\Debug\net48\DirectoryAnalyzer.UI.exe
```

## Configuração TLS e mTLS

### Broker (`src/DirectoryAnalyzer.Broker/appsettings.json`)

```json
{
  "Broker": {
    "EnableClientCertificateValidation": false,
    "AllowedThumbprints": [],
    "TrustedCaThumbprints": []
  }
}
```

- **TLS 1.2+** é obrigatório (Kestrel já força).
- **mTLS opcional**: habilite `EnableClientCertificateValidation` e preencha `AllowedThumbprints` ou `TrustedCaThumbprints`.

### Agent (`AgentConfig`)

- `BrokerUrl`: URL do hub (ex: `https://localhost:5001/agent-hub`)
- `BrokerClientCertificateThumbprint`: opcional, usado quando mTLS no Broker está habilitado.

### UI (`src/DirectoryAnalyzer.UI/PrototypeConfigs/brokerclientsettings.json`)

```json
{
  "BrokerBaseUrl": "https://localhost:5001",
  "ClientCertThumbprint": "",
  "RequestTimeoutSeconds": 30,
  "PollIntervalSeconds": 2
}
```

- Para mTLS, preencha `ClientCertThumbprint`.

## Contrato único (DTOs)

Os contratos de dados ficam em `src/DirectoryAnalyzer.Contracts`.
A UI consome apenas `ModuleResult` e `ResultItem` (sem `PSObject`/`IDictionary` diretos em ViewModels).

## Exportações

A UI exporta a partir dos resultados normalizados (`ModuleResult`):
- CSV / XML / HTML / SQL

## Logs

- **UI**: `LogService` por módulo.
- **Agent**: logs estruturados com `CorrelationId` e `AgentId`.

## Regras Read-Only (não negociar)

- Sem alterações em AD/GPO/IIS/Políticas locais/Serviços/Tarefas/Registro.
- Coletas apenas com permissões mínimas necessárias.
- Sem credenciais hardcoded.

---

> Para detalhes adicionais, consulte `docs/`.
