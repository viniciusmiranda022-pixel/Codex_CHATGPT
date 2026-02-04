# Runbook de implantação

## Padrão oficial de hosting do agente
O agente deve ser hospedado como serviço Windows instalado via MSI do projeto WiX. O MSI cria o serviço, grava `agentsettings.json` e registra parâmetros no registry.

O modo console existe apenas para laboratório e validação local, pois o código executa em modo interativo quando `Environment.UserInteractive` é verdadeiro.

## Pré-requisitos
### Aplicativo WPF
1. Windows com .NET Framework 4.8.
2. PowerShell com módulos exigidos pelos módulos que serão usados.

### Agente
1. Windows com .NET Framework 4.8.
2. Certificado de servidor em `LocalMachine\My`, com thumbprint configurado em `agentsettings.json`.
3. Certificado de cliente em `CurrentUser\My` para a UI, ou conforme o cliente que fará a chamada.
4. Porta HTTPS liberada no firewall, padrão 8443.

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
1. Serviço instalado e em execução com `Get-Service DirectoryAnalyzerAgent`.
2. Arquivo de configuração criado em `%ProgramData%\DirectoryAnalyzerAgent\agentsettings.json`.
3. Log do agente criado no caminho configurado, padrão `%ProgramData%\DirectoryAnalyzerAgent\Logs\agent.log`.

## Configuração do cliente do agente
O arquivo de configuração do cliente da UI é criado automaticamente quando não existe.
1. Caminho padrão em `%ProgramData%\DirectoryAnalyzerAgent\agentclientsettings.json`.
2. Preencher endpoint do agente e thumbprints allow list.

## Precedência de configuração
### Agente
1. Se existir `%ProgramData%\DirectoryAnalyzerAgent\agentsettings.json`, ele é usado.
2. Se não existir no path novo e existir no path legado `%ProgramData%\DirectoryAnalyzer\agentsettings.json`, o arquivo é copiado para o path novo e o novo é usado.
3. Se existir somente na base do executável, esse arquivo é usado.
4. Se nenhum existir, o agente cria `%ProgramData%\DirectoryAnalyzerAgent\agentsettings.json` com defaults e registry.
5. Após ler o JSON, valores em `HKLM\SOFTWARE\DirectoryAnalyzer\Agent` sobrescrevem os campos quando preenchidos.

### AnalyzerClient
1. Se existir `%ProgramData%\DirectoryAnalyzerAgent\agentclientsettings.json`, ele é usado.
2. Se não existir no path novo e existir no path legado `%ProgramData%\DirectoryAnalyzer\agentclientsettings.json`, o arquivo é copiado para o path novo e o novo é usado.
3. Se existir somente na base do executável, esse arquivo é usado.
4. Se nenhum existir, o comando retorna erro ao tentar carregar o JSON.

## Validação rápida pós instalação
Checklist objetivo após instalar e configurar certificados.
1. Executar no host do agente `DirectoryAnalyzer.Agent.exe --doctor`.
2. Executar no host do cliente `DirectoryAnalyzer.AnalyzerClient.exe --doctor`.
3. Confirmar no console e nos logs.
   3.1. Paths resolvidos para config e log.
   3.2. Precedência aplicada e fonte vencedora.
   3.3. Status de migração de config.
   3.4. Validação de escrita no diretório de log.
   3.5. Validação de JSON e campos obrigatórios.
   3.6. Validação de URL e thumbprint quando aplicável.
4. Logs esperados.
   4.1. Agente em `%ProgramData%\DirectoryAnalyzerAgent\Logs\agent.log`.
   4.2. Cliente em `%ProgramData%\DirectoryAnalyzerAgent\Logs\analyzerclient.log`.
5. Exit code esperado.
   5.1. Valor 0 quando todas as validações passam.
   5.2. Valor 1 quando há falha crítica.

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
1. Testar porta do agente com `Test-NetConnection <host-agente> -Port 8443`.
2. Validar mTLS com o `AnalyzerClient`.
3. No WPF, habilitar Agent Mode e executar "Agent Inventory".

## Alternativa de laboratório
Execução em modo console do agente, sem serviço Windows.
```powershell
DirectoryAnalyzer.Agent.exe
```
