# DirectoryAnalyzer Agent MSI (WiX)

## Silent install examples

Install with explicit listen port, server certificate thumbprint, and allowed client certificates:

```powershell
msiexec /i DirectoryAnalyzer.Agent.msi /qn LISTENPORT=8443 CERTTHUMBPRINT="<SERVER_CERT_THUMBPRINT>" ALLOWEDCLIENTCERTTHUMBPRINTS="<CLIENT_CERT_THUMBPRINT_1>;<CLIENT_CERT_THUMBPRINT_2>" /l*v install.log
```

Install with a trusted CA thumbprint list and create the firewall rule:

```powershell
msiexec /i DirectoryAnalyzer.Agent.msi /qn LISTENPORT=8443 CERTTHUMBPRINT="<SERVER_CERT_THUMBPRINT>" TRUSTEDCATHUMBPRINTS="<CA_THUMBPRINT_1>;<CA_THUMBPRINT_2>" CREATEFIREWALLRULE=1 /l*v install.log
```

Uninstall silently:

```powershell
msiexec /x DirectoryAnalyzer.Agent.msi /qn /l*v uninstall.log
```

## Configuration output

The installer writes `agentsettings.json` to:

```
%ProgramData%\DirectoryAnalyzerAgent\agentsettings.json
```

The file includes values derived from `LISTENPORT`, `CERTTHUMBPRINT`, `ALLOWEDCLIENTCERTTHUMBPRINTS`, and `TRUSTEDCATHUMBPRINTS`.
