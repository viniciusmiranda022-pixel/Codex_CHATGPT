# Glossário

## Finalidade
Definir termos técnicos usados na documentação e no código.

## Público-alvo
* Todas as áreas (segurança, infra, desenvolvimento, gestão).

## Premissas
* Termos seguem nomenclatura Windows/AD.

## Ponteiros de código (provas)
* Termos de agente e contratos: `DirectoryAnalyzer.Agent.Contracts/AgentContracts.cs`.
* Termos WinRM/CIM: módulos PowerShell nos `*.xaml.cs`.

**Agente**: Serviço/console que expõe API HTTPS para coleta AD.

**mTLS**: Mutual TLS; cliente e servidor apresentam certificados.

**Thumbprint**: Hash do certificado usado para allowlist/pinning.

**Nonce**: Valor único por request, usado para anti-replay.

**WinRM**: Windows Remote Management (Invoke-Command remoto).

**CIM/WMI**: Interface de gerenciamento do Windows para inventário.

**GPO**: Group Policy Object.

**PDC**: Primary Domain Controller.

## LIMITAÇÕES ATUAIS
* Nenhuma específica (definições diretas).

## COMO VALIDAR
* Conferir termos nos arquivos `AgentContracts` e módulos PowerShell.
