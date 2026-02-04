# Runbook de implantação

## Padrão oficial de hosting do agente
O agente deve ser hospedado como serviço Windows instalado via MSI do projeto WiX. O MSI cria o serviço, grava `agentsettings.json` e registra parâmetros no registry.

Modo console existe apenas para laboratório e validação local, pois o código executa em modo interativo quando `Environment.UserInteractive` é verdadeiro.

## Pré-requisitos
### Aplicativo WPF
- Windows com .NET Framework 4.8.
- PowerShell com módulos exigidos pelos módulos que serão usados.

### Agente
- Windows com .NET Framework 4.8.
- Certificado de servidor em `LocalMachine\My`, com thumbprint configurado em `agentsettings.json`.
- Certificado de cliente em `CurrentUser\My` para a UI, ou conforme o cliente que fará a chamada.
- Porta HTTPS liberada no firewall, padrão 8443.

## Certificados
### Servidor do agente
1. Emitir um certificado X.509 para o endpoint do agente, com DNS no Subject ou SAN.
2. Instalar em `LocalMachine\My`.
3. Registrar o thumbprint em `%ProgramData%\DirectoryAnalyzerAgent\agentsettings.json`.

### Cliente do agente
1. Emitir certificado X.509 para o cliente.
2. Instalar em `CurrentUser\My` da máquina do operador.
3. Registrar o thumbprint em `%ProgramData%\DirectoryAnalyzerAgent\agentclientsettings.json`.

## Binding HTTPS
Exemplo de binding com netsh, com client cert negotiation habilitado.
```powershell
netsh http add sslcert ipport=0.0.0.0:8443 certhash=<THUMBPRINT_DO_AGENTE> appid='{C33B62F9-9B7E-4A9C-BBE5-DAA71B4A6901}' clientcertnegotiation=enable
```

## Instalação do agente via MSI
### Build do MSI
```powershell
msbuild Installer\DirectoryAnalyzer.Agent.wixproj /p:Configuration=Release
```

### Instalação com parâmetros
```powershell
msiexec /i Installer\bin\Release\DirectoryAnalyzer.Agent.msi /qn \
  SERVICEACCOUNT="CONTOSO\\gmsaDirectoryAnalyzer$" SERVICEPASSWORD="" \
  CERTTHUMBPRINT="<AGENT_CERT_THUMBPRINT>" ALLOWEDCLIENTCERTTHUMBPRINTS="<CLIENT_CERT_THUMBPRINT>" \
  LISTENPORT=8443 CREATEFIREWALLRULE=1
```

### Validações pós instalação
- Serviço instalado e em execução, `Get-Service DirectoryAnalyzerAgent`.
- Arquivo de configuração criado, `%ProgramData%\DirectoryAnalyzerAgent\agentsettings.json`.
- Log do agente criado no caminho configurado, padrão `%ProgramData%\DirectoryAnalyzerAgent\Logs\agent.log`.

## Configuração do cliente do agente
O arquivo de configuração do cliente da UI é criado automaticamente se não existir.
- Caminho padrão, `%ProgramData%\DirectoryAnalyzerAgent\agentclientsettings.json`.
- Preencher endpoint do agente e thumbprints allow-list.

## Precedência de configuração
### Agente
1. Se existir `%ProgramData%\DirectoryAnalyzerAgent\agentsettings.json`, ele é usado.
2. Se não existir no path novo e existir no path legado `%ProgramData%\DirectoryAnalyzer\agentsettings.json`, o arquivo é copiado para o path novo e o novo é usado.
3. Se existir somente na base do executável, esse arquivo é usado.
4. Se nenhum existir, o agente cria o arquivo em `%ProgramData%\DirectoryAnalyzerAgent\agentsettings.json` com defaults e registry.
5. Após ler o JSON, valores em `HKLM\SOFTWARE\DirectoryAnalyzer\Agent` sobrescrevem os campos quando preenchidos.

### Cliente do agente na UI
1. Se existir `%ProgramData%\DirectoryAnalyzerAgent\agentclientsettings.json`, ele é usado.
2. Se não existir no path novo e existir no path legado `%ProgramData%\DirectoryAnalyzer\agentclientsettings.json`, o arquivo é copiado para o path novo e o novo é usado.
3. Se existir somente na base do executável, esse arquivo é usado.
4. Se nenhum existir, a UI cria um JSON default e passa a usá-lo.

## Validação rápida pós-instalação
Checklist objetivo após instalar e configurar certificados.
1. Executar no host do agente: `DirectoryAnalyzer.Agent.exe --doctor`.
2. Executar no host do cliente: `DirectoryAnalyzer.AnalyzerClient.exe --doctor`.
3. Confirmar no console e nos logs:
   - paths resolvidos para config e log.
   - precedência aplicada e fonte vencedora.
   - status de migração de config.
   - validação de escrita no diretório de log.
   - validação de JSON e campos obrigatórios.
4. Logs esperados:
   - agente: `%ProgramData%\DirectoryAnalyzerAgent\Logs\agent.log`.
   - cliente: `%ProgramData%\DirectoryAnalyzerAgent\Logs\analyzerclient.log`.
5. Exit code esperado:
   - 0 quando todas as validações passam.
   - 1 quando há falha crítica.

## Implantação do aplicativo WPF
### Build
```powershell
msbuild .\DirectoryAnalyzer.sln /t:Restore,Build /p:Configuration=Release
```

### Execução
```powershell
.\bin\Release\net48\DirectoryAnalyzer.exe
```

## Validações funcionais
- Testar porta do agente, `Test-NetConnection <host-agente> -Port 8443`.
- Validar mTLS com o `AnalyzerClient`.
- No WPF, habilitar Agent Mode e executar "Agent Inventory".

## Alternativa de laboratório
Execução em modo console do agente, sem serviço Windows.
```powershell
DirectoryAnalyzer.Agent.exe
```
