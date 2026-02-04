namespace DirectoryAnalyzer.Collectors
{
    public static class PowerShellScripts
    {
        public static string GetScriptForModule(string moduleName)
        {
            switch (moduleName)
            {
                case "ScheduledTasksAnalyzer":
                    return ScheduledTasksScript();
                case "SmbSharesAnalyzer":
                    return SmbSharesScript();
                case "InstalledServicesAnalyzer":
                    return InstalledServicesScript();
                case "LocalProfilesAnalyzer":
                    return LocalProfilesScript();
                case "LocalSecurityPolicyAnalyzer":
                    return LocalSecurityPolicyScript();
                case "IisAppPoolsAnalyzer":
                    return IisAppPoolsScript();
                case "ProxyAddressAnalyzer":
                    return ProxyAddressesScript();
                case "TrustsAnalyzer":
                    return TrustsScript();
                case "GpoAnalyzer":
                    return GpoScript();
                case "DnsAnalyzer":
                    return DnsScript();
                default:
                    return null;
            }
        }

        private static string ScheduledTasksScript() => @"
            param([string]$AttributeName, [string]$AttributeValue)
            Import-Module ActiveDirectory -ErrorAction SilentlyContinue; if (-not (Get-Module ActiveDirectory)) { throw 'Módulo ActiveDirectory não encontrado.' }
            
            try { 
                $serverList = Get-ADComputer -Filter ""$AttributeName -eq '$AttributeValue'"" | Select-Object -ExpandProperty Name 
            } catch { 
                throw ""Falha ao buscar computadores no AD: $($_.Exception.Message)"" 
            }

            if (-not $serverList) { Write-Warning ""Nenhum computador encontrado com os critérios.""; return }

            $allResults = foreach ($serverName in $serverList) {
                if (-not (Test-Connection -ComputerName $serverName -Count 1 -Quiet -ErrorAction SilentlyContinue)) {
                    [PSCustomObject]@{ ComputerName = $serverName; State = 'ERRO'; TaskName = 'CONEXÃO FALHOU'; UserId = 'N/A'; TaskPath = 'Servidor inacessível (ping falhou)' }; continue
                }

                $scriptBlock = {
                    Get-ScheduledTask | Where-Object { $_.TaskPath -notlike '\\Microsoft*' } | ForEach-Object {
                        [PSCustomObject]@{
                            ComputerName = $env:COMPUTERNAME;
                            State        = $_.State;
                            TaskName     = $_.TaskName;
                            UserId       = $_.Principal.UserId;
                            TaskPath     = $_.TaskPath;
                        }
                    }
                }

                try { 
                    Invoke-Command -ComputerName $serverName -ScriptBlock $scriptBlock -ErrorAction Stop 
                } catch { 
                    [PSCustomObject]@{ ComputerName = $serverName; State = 'ERRO'; TaskName = 'EXECUÇÃO REMOTA'; UserId = 'N/A'; TaskPath = $_.Exception.Message }
                }
            }
            $allResults
        ";

        private static string SmbSharesScript() => @"
            param([string]$AttributeName, [string]$AttributeValue)
            Import-Module ActiveDirectory -ErrorAction SilentlyContinue; if (-not (Get-Module ActiveDirectory)) { throw 'Módulo ActiveDirectory não encontrado.' }
            try { $serverList = Get-ADComputer -Filter ""$AttributeName -eq '$AttributeValue'"" | Select-Object -ExpandProperty Name } catch { throw ""Falha ao buscar computadores no AD: $($_.Exception.Message)"" }
            if (-not $serverList) { Write-Warning ""Nenhum computador encontrado.""; return }
            $allResults = foreach ($serverName in $serverList) {
                if (-not (Test-Connection -ComputerName $serverName -Count 1 -Quiet -ErrorAction SilentlyContinue)) {
                    [PSCustomObject]@{ ComputerName = $serverName; ShareName = 'N/A'; SharePath = 'N/A'; IdentityReference = 'ERRO DE CONEXÃO'; AccessControlType = 'Servidor inacessível (ping falhou)'; FileSystemRights = 'N/A'; IsInherited = $false }; continue
                }
                $scriptBlock = {
                    $localResults = @(); $shares = Get-CimInstance -ClassName Win32_Share -ErrorAction SilentlyContinue | Where-Object { $_.Type -eq 0 -and $_.Name -notlike '*$' };
                    if ($shares) { foreach ($share in $shares) {
                        $folderPath = $share.Path; if(-not ([string]::IsNullOrWhiteSpace($folderPath))) {
                            try { $acl = Get-Acl -Path $folderPath -ErrorAction Stop; foreach ($ace in $acl.Access) { $localResults += [PSCustomObject]@{ ComputerName = $env:COMPUTERNAME; ShareName = $share.Name; SharePath = $share.Path; IdentityReference = $ace.IdentityReference.Value; AccessControlType = $ace.AccessControlType.ToString(); FileSystemRights = $ace.FileSystemRights.ToString(); IsInherited = $ace.IsInherited } }
                            } catch { $localResults += [PSCustomObject]@{ ComputerName = $env:COMPUTERNAME; ShareName = $share.Name; SharePath = $share.Path; IdentityReference = ""ERRO DE ACESSO LOCAL ÀS PERMISSÕES""; AccessControlType = $_.Exception.Message; FileSystemRights = 'N/A'; IsInherited = $false } }
                        }
                    } }
                    return $localResults
                }
                try { Invoke-Command -ComputerName $serverName -ScriptBlock $scriptBlock -ErrorAction Stop } catch { [PSCustomObject]@{ ComputerName = $serverName; ShareName = 'N/A'; SharePath = 'N/A'; IdentityReference = 'ERRO DE EXECUÇÃO REMOTA (Invoke-Command)'; AccessControlType = $_.Exception.Message; FileSystemRights = 'N/A'; IsInherited = $false } }
            }
            $allResults
        ";

        private static string InstalledServicesScript() => @"
            param([string]$AttributeName, [string]$AttributeValue)
            Import-Module ActiveDirectory -ErrorAction SilentlyContinue; if (-not (Get-Module ActiveDirectory)) { throw 'Módulo ActiveDirectory não encontrado.' }
            try { $serverList = Get-ADComputer -Filter ""$AttributeName -eq '$AttributeValue'"" | Select-Object -ExpandProperty Name } catch { throw ""Falha ao buscar computadores no AD: $($_.Exception.Message)"" }
            if (-not $serverList) { return }

            $allResults = foreach ($serverName in $serverList) {
                if (-not (Test-Connection -ComputerName $serverName -Count 1 -Quiet -ErrorAction SilentlyContinue)) {
                    [PSCustomObject]@{ ComputerName = $serverName; DisplayName = 'ERRO DE CONEXÃO'; Name = 'N/A'; State = 'N/A'; StartMode = 'N/A'; ContaDeServico = 'Servidor inacessível (ping falhou)' }; continue
                }
                $scriptBlock = { Get-CimInstance -ClassName Win32_Service | Select-Object @{N='ComputerName';E={$env:COMPUTERNAME}}, DisplayName, Name, State, StartMode, @{N='ContaDeServico';E={$_.StartName}}, PathName, Description, ServiceType }
                try { Invoke-Command -ComputerName $serverName -ScriptBlock $scriptBlock -ErrorAction Stop } catch { [PSCustomObject]@{ ComputerName = $serverName; DisplayName = 'ERRO DE EXECUÇÃO REMOTA'; Name = 'N/A'; State = 'N/A'; StartMode = 'N/A'; ContaDeServico = $_.Exception.Message } }
            }
            $allResults
        ";

        private static string LocalProfilesScript() => @"
            param([string]$AttributeName, [string]$AttributeValue)
            Import-Module ActiveDirectory -ErrorAction SilentlyContinue; if (-not (Get-Module ActiveDirectory)) { throw 'Módulo ActiveDirectory não encontrado.' }
            try { $serverList = Get-ADComputer -Filter ""$AttributeName -eq '$AttributeValue'"" | Select-Object -ExpandProperty Name } catch { throw ""Falha ao buscar computadores no AD: $($_.Exception.Message)"" }
            if (-not $serverList) { Write-Warning ""Nenhum computador encontrado.""; return }

            $allProfiles = @()
            foreach ($serverName in $serverList) {
                if (-not (Test-Connection -ComputerName $serverName -Count 1 -Quiet -ErrorAction SilentlyContinue)) {
                    $allProfiles += [PSCustomObject]@{ ComputerName = $serverName; AccountName = 'N/A'; SID = 'N/A'; LocalPath = 'ERRO DE CONEXÃO'; LastUseTime = $null }; continue
                }
                try {
                    $profiles = Get-CimInstance -ClassName Win32_UserProfile -ComputerName $serverName -Filter 'Special = false' -ErrorAction Stop
                    if (-not $profiles) { continue }
                    foreach ($profile in $profiles) {
                        $accountName = 'SID não resolvido'
                        try {
                            $sidObject = New-Object System.Security.Principal.SecurityIdentifier($profile.SID)
                            $accountName = $sidObject.Translate([System.Security.Principal.NTAccount]).Value
                        } catch { }
                        
                        $allProfiles += [PSCustomObject]@{
                            ComputerName = $serverName
                            AccountName = $accountName
                            SID = $profile.SID
                            LocalPath = $profile.LocalPath
                            LastUseTime = $profile.LastUseTime
                        }
                    }
                } catch {
                    $allProfiles += [PSCustomObject]@{ ComputerName = $serverName; AccountName = 'N/A'; SID = 'N/A'; LocalPath = ""ERRO GERAL DE COLETA: $($_.Exception.Message)""; LastUseTime = $null }
                }
            }
            $allProfiles
        ";

        private static string LocalSecurityPolicyScript() => @"
            param([string]$AttributeName, [string]$AttributeValue)
            
            function Convert-PolicyValue {
                param($PolicyArea, $SettingName, $SettingValue)
                $returnObject = @{ Value = $SettingValue; Description = '' }
                try {
                    switch ($PolicyArea) {
                        'System Access' {
                            switch ($SettingName) {
                                'MinimumPasswordAge' { $returnObject.Value = ""$SettingValue dias""; $returnObject.Description = 'Tempo mínimo que uma senha deve ser mantida.' }; 'MaximumPasswordAge' { $returnObject.Value = ""$SettingValue dias""; $returnObject.Description = 'Tempo máximo de vida de uma senha.' }
                                'MinimumPasswordLength' { $returnObject.Value = if ($SettingValue -eq '0') { 'Não requer (0)' } else { ""$SettingValue caracteres"" }; $returnObject.Description = 'Número mínimo de caracteres da senha.' }
                                'PasswordHistorySize' { $returnObject.Value = if ($SettingValue -eq '0') { 'Nenhum histórico (0)' } else { ""$SettingValue senhas lembradas"" }; $returnObject.Description = 'Impede a reutilização de senhas recentes.' }
                                'LockoutBadCount' { $returnObject.Value = if ($SettingValue -eq '0') { 'Nenhum bloqueio (0)' } else { ""$SettingValue tentativas inválidas"" }; $returnObject.Description = 'Número de tentativas falhas antes de bloquear a conta.' }
                                'LockoutDuration' { $returnObject.Value = if ($SettingValue -eq '0') { 'Administrador deve desbloquear' } else { ""$SettingValue minutos"" }; $returnObject.Description = 'Duração do bloqueio da conta.' }
                                'PasswordComplexity' { $returnObject.Value = if ($SettingValue -eq '1') { 'Habilitada' } else { 'Desabilitada' }; $returnObject.Description = 'Exige complexidade (maiúsculas, minúsculas, números, etc).' }
                                'ClearTextPassword' { $returnObject.Value = if ($SettingValue -eq '1') { 'Habilitado (inseguro)' } else { 'Desabilitado' }; $returnObject.Description = 'Permite armazenar senhas de forma reversível.' }
                                'EnableAdminAccount' { $returnObject.Value = if ($SettingValue -eq '1') { 'Habilitada' } else { 'Desabilitada' }; $returnObject.Description = 'Status da conta de Administrador local padrão.' }
                                'EnableGuestAccount' { $returnObject.Value = if ($SettingValue -eq '1') { 'Habilitada' } else { 'Desabilitada' }; $returnObject.Description = 'Status da conta de Convidado (Guest) local padrão.' }
                            }
                        }
                        'Event Audit' {
                            $returnObject.Description = 'Define quais eventos são registrados no log de segurança.'
                            switch ($SettingValue) { '0' { $returnObject.Value = 'Sem Auditoria' }; '1' { $returnObject.Value = 'Sucesso' }; '2' { $returnObject.Value = 'Falha' }; '3' { $returnObject.Value = 'Sucesso e Falha' } }
                        }
                        'Privilege Rights' {
                            $returnObject.Description = 'Define quais contas/grupos podem executar ações privilegiadas.'
                            if ($SettingValue) {
                                $wellKnownSids = @{ 'S-1-5-32-544' = 'BUILTIN\Administrators'; 'S-1-5-32-545' = 'BUILTIN\Users'; 'S-1-5-18' = 'NT AUTHORITY\SYSTEM'; 'S-1-1-0' = 'Everyone'; 'S-1-5-11' = 'NT AUTHORITY\Authenticated Users' }
                                $resolvedNames = @(); $sids = $SettingValue.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ }
                                foreach ($sidString in $sids) {
                                    $cleanSid = $sidString.TrimStart('*'); try {
                                        if ($wellKnownSids.ContainsKey($cleanSid)) { $resolvedNames += $wellKnownSids[$cleanSid] }
                                        else { $sid = New-Object System.Security.Principal.SecurityIdentifier($cleanSid); $resolvedNames += $sid.Translate([System.Security.Principal.NTAccount]).Value }
                                    } catch { $resolvedNames += $sidString }
                                }
                                $returnObject.Value = $resolvedNames -join ', '
                            }
                        }
                        'Registry Values' {
                            if ($SettingValue -match '^(\d+),(.*)$') {
                                $type = $matches[1]; $value = $matches[2].Trim('"'); $finalValue = $value; $translated = ''
                                if ($type -eq '4') {
                                    $dwordValue = [System.Convert]::ToInt32($value, 10); $binMap = @{ 0 = 'Desabilitado'; 1 = 'Habilitado' }
                                    $enabled = if ($binMap.ContainsKey($dwordValue)) { "" ($($binMap[$dwordValue]))"" } else { '' }
                                    $translated = ""DWORD:$dwordValue$enabled""
                                } elseif ($type -eq '7') { $finalValue = $($value.Split([char]0,[char]44) -join '; ').Trim('; '); $translated = ""Multi-String: $finalValue"" }
                                else { $translated = ""String: $finalValue"" }
                                $returnObject.Value = $translated; $returnObject.Description = 'Configuração do registro do Windows.'
                            }
                        }
                    }
                } catch {}
                return $returnObject
            }

            Import-Module ActiveDirectory -ErrorAction SilentlyContinue; if (-not (Get-Module ActiveDirectory)) { throw 'Módulo ActiveDirectory não encontrado.' }
            try { $serverList = Get-ADComputer -Filter ""$AttributeName -eq '$AttributeValue'"" | Select-Object -ExpandProperty Name } catch { throw ""Falha ao buscar computadores no AD: $($_.Exception.Message)"" }
            if (-not $serverList) { return }

            $allResults = foreach ($serverName in $serverList) {
                if (-not (Test-Connection -ComputerName $serverName -Count 1 -Quiet -ErrorAction SilentlyContinue)) {
                    [PSCustomObject]@{ ComputerName = $serverName; PolicyArea = 'Erro'; SettingName = 'Conexão'; SettingValue = 'Servidor inacessível (ping falhou)'; Descricao = '' }; continue
                }
                $scriptBlock = {
                    $tempFile = Join-Path $env:TEMP ""$(New-Guid).inf""
                    try { 
                        secedit.exe /export /cfg $tempFile /quiet
                        if (Test-Path $tempFile) { Get-Content -Path $tempFile -Encoding Unicode }
                    } finally { if (Test-Path $tempFile) { Remove-Item -Path $tempFile -Force } }
                }
                try {
                    $infContent = Invoke-Command -ComputerName $serverName -ScriptBlock $scriptBlock -ErrorAction Stop
                    $currentArea = 'N/A'
                    foreach($line in $infContent){
                        if($line -match '^\[(.*)\]$'){ $currentArea = $matches[1].Trim() }
                        elseif($line -match '^(.*?)\s*=\s*(.*)$'){
                            $settingName = $matches[1].Trim(); $rawSettingValue = $matches[2].Trim()
                            $translationObject = Convert-PolicyValue -PolicyArea $currentArea -SettingName $settingName -SettingValue $rawSettingValue
                            [PSCustomObject]@{
                                ComputerName = $serverName; PolicyArea = $currentArea; SettingName = $settingName;
                                SettingValue = $translationObject.Value; Descricao = $translationObject.Description
                            }
                        }
                    }
                } catch {
                    [PSCustomObject]@{ ComputerName = $serverName; PolicyArea = 'Erro'; SettingName = 'Execução Remota'; SettingValue = ""ERRO GERAL DE COLETA: $($_.Exception.Message)""; Descricao = '' }
                }
            }
            $allResults
        ";

        private static string IisAppPoolsScript() => @"
            param([string]$AttributeName, [string]$AttributeValue)
            Import-Module ActiveDirectory -ErrorAction SilentlyContinue; if (-not (Get-Module ActiveDirectory)) { throw 'Módulo ActiveDirectory não encontrado.' }
            Import-Module WebAdministration -ErrorAction SilentlyContinue; if (-not (Get-Module WebAdministration)) { throw 'Módulo WebAdministration não encontrado.' }

            try { 
                $serverList = Get-ADComputer -Filter ""$AttributeName -eq '$AttributeValue'"" | Select-Object -ExpandProperty Name 
            } catch { 
                throw ""Falha ao buscar computadores no AD: $($_.Exception.Message)"" 
            }

            if (-not $serverList) { Write-Warning ""Nenhum computador encontrado com os critérios.""; return }

            $allResults = foreach ($serverName in $serverList) {
                if (-not (Test-Connection -ComputerName $serverName -Count 1 -Quiet -ErrorAction SilentlyContinue)) {
                    [PSCustomObject]@{ ComputerName = $serverName; ApplicationPool = 'ERRO DE CONEXÃO'; Status = 'N/A'; CustomIdentity = 'Servidor inacessível (ping falhou)'; SitesVinculados = 'N/A' }; continue
                }

                $scriptBlock = {
                    Import-Module WebAdministration -ErrorAction SilentlyContinue
                    Get-ChildItem IIS:\\AppPools | Select-Object Name, State, @{N='IdentityType';E={$_.processModel.identityType}}, @{N='UserName';E={$_.processModel.userName}}, @{N='QueueLength';E={$_.queueLength}}, @{N='AutoStart';E={$_.autoStart}}
                }

                try {
                    Invoke-Command -ComputerName $serverName -ScriptBlock $scriptBlock -ErrorAction Stop
                } catch {
                    [PSCustomObject]@{ ComputerName = $serverName; ApplicationPool = 'ERRO DE EXECUÇÃO REMOTA'; Status = 'N/A'; CustomIdentity = $_.Exception.Message; SitesVinculados = 'N/A' }
                }
            }
            $allResults
        ";

        private static string ProxyAddressesScript() => @"
            param([string]$AttributeName, [string]$AttributeValue)
            Import-Module ActiveDirectory -ErrorAction SilentlyContinue
            if (-not (Get-Module ActiveDirectory)) { throw 'Módulo ActiveDirectory não encontrado.' }

            $filter = ""$AttributeName -eq '$AttributeValue'""
            $users = Get-ADUser -Filter $filter -Properties ProxyAddresses, UserPrincipalName, SamAccountName

            $flatList = @()
            foreach ($user in $users) {
                if ($user.ProxyAddresses) {
                    foreach ($address in $user.ProxyAddresses) {
                        $isPrimary = $address -cmatch '^SMTP:'
                        $flatList += [PSCustomObject]@{
                            UserPrincipalName = $user.UserPrincipalName
                            SamAccountName    = $user.SamAccountName
                            ProxyAddress      = $address.ToString()
                            IsPrimarySmtp     = $isPrimary
                        }
                    }
                }
            }
            return $flatList
        ";

        private static string TrustsScript() => @"
            Import-Module ActiveDirectory -ErrorAction SilentlyContinue
            if (-not (Get-Module ActiveDirectory)) { throw 'Módulo ActiveDirectory não encontrado.' }
            Get-ADTrust -Filter * | Select-Object Source, Target, Direction, TrustType, IsTransitive
        ";

        private static string GpoScript() => @"
            Import-Module GroupPolicy -ErrorAction SilentlyContinue
            if (-not (Get-Module GroupPolicy)) { throw 'Módulo GroupPolicy não encontrado.' }
            Import-Module ActiveDirectory -ErrorAction SilentlyContinue
            if (-not (Get-Module ActiveDirectory)) { throw 'Módulo ActiveDirectory não encontrado.' }

            $gpos = Get-GPO -All
            $gpoResumo = @()
            $gpoLinks = @()
            $gpoDelegacoes = @()
            $gpoSecurityFiltering = @()
            $gpoWmiFilters = @()

            foreach ($gpo in $gpos) {
                $gpoResumo += [PSCustomObject]@{
                    DisplayName = $gpo.DisplayName
                    Id = $gpo.Id
                    Owner = $gpo.Owner
                    CreationTime = $gpo.CreationTime
                    ModificationTime = $gpo.ModificationTime
                    GpoStatus = $gpo.GpoStatus
                    Description = $gpo.Description
                }

                $links = Get-GPOLink -Guid $gpo.Id -ErrorAction SilentlyContinue
                if ($links) {
                    foreach ($link in $links) {
                        $gpoLinks += [PSCustomObject]@{
                            DisplayName = $gpo.DisplayName
                            Link = $link.Target
                            Enabled = $link.Enabled
                            Enforced = $link.Enforced
                        }
                    }
                }

            $delegacoes = Get-GPPermissions -Guid $gpo.Id -All -ErrorAction SilentlyContinue
            if ($delegacoes) {
                    foreach ($delegate in $delegacoes) {
                        $gpoDelegacoes += [PSCustomObject]@{
                            DisplayName = $gpo.DisplayName
                            Trustee = $delegate.Trustee.Name
                            Permission = $delegate.Permission
                        }
                    }
                }

                $securityFiltering = Get-GPPermissions -Guid $gpo.Id -All -ErrorAction SilentlyContinue | Where-Object { $_.Permission -eq 'GpoApply' }
                if ($securityFiltering) {
                    foreach ($filter in $securityFiltering) {
                        $gpoSecurityFiltering += [PSCustomObject]@{
                            DisplayName = $gpo.DisplayName
                            Trustee = $filter.Trustee.Name
                            Permission = $filter.Permission
                        }
                    }
                }

                $wmi = Get-GPWmiFilter -Guid $gpo.Id -ErrorAction SilentlyContinue
                if ($wmi) {
                    $gpoWmiFilters += [PSCustomObject]@{
                        DisplayName = $gpo.DisplayName
                        WmiFilter = $wmi.Name
                    }
                }
            }

            [PSCustomObject]@{
                ResumoJson = ($gpoResumo | ConvertTo-Json -Depth 6);
                LinksJson = ($gpoLinks | ConvertTo-Json -Depth 6);
                DelegacoesJson = ($gpoDelegacoes | ConvertTo-Json -Depth 6);
                SecurityFilteringJson = ($gpoSecurityFiltering | ConvertTo-Json -Depth 6);
                WmiFiltersJson = ($gpoWmiFilters | ConvertTo-Json -Depth 6)
            }
        ";

        private static string DnsScript() => @"
            Import-Module DnsServer -ErrorAction SilentlyContinue
            if (-not (Get-Module DnsServer)) { throw 'Módulo DnsServer não encontrado.' }
            $pdc = (Get-ADDomainController -Discover -Service PrimaryDC).HostName

            $zonas = Get-DnsServerZone -ComputerName $pdc | Select-Object ZoneName, ZoneType, IsReverseLookupZone, DynamicUpdate
            $forwarders = Get-DnsServerForwarder -ComputerName $pdc | Select-Object @{N='IPAddress';E={($_.IPAddress | ForEach-Object { $_.IPAddressToString }) -join '; '}}, UseRootHint, EnableReordering, Timeout

            $allRecords = @()
            foreach($zone in $zonas){
                $recordsInZone = Get-DnsServerResourceRecord -ZoneName $zone.ZoneName -ComputerName $pdc -ErrorAction SilentlyContinue
                if($recordsInZone){
                    foreach ($record in $recordsInZone) {
                        $recordDataValue = 'N/A'
                        if ($record.RecordData) {
                            switch($record.RecordType){
                                'A'     { $recordDataValue = '' + $record.RecordData.IPV4Address }
                                'AAAA'  { $recordDataValue = '' + $record.RecordData.IPV6Address }
                                'CNAME' { $recordDataValue = $record.RecordData.HostNameAlias }
                                'MX'    { $recordDataValue = ""$($record.RecordData.MailExchange) (Pref: $($record.RecordData.Preference))"" }
                                'NS'    { $recordDataValue = $record.RecordData.NameServer }
                                'PTR'   { $recordDataValue = $record.RecordData.PtrDomainName }
                                'SRV'   { $recordDataValue = ""$($record.RecordData.DomainName):$($record.RecordData.Port)"" }
                                'SOA'   { $recordDataValue = ""Servidor Principal: $($record.RecordData.PrimaryServer)"" }
                                default { $recordDataValue = ""Tipo não mapeado: $($record.RecordType)"" }
                            }
                        }
                        $allRecords += [PSCustomObject]@{
                            ZoneName   = $zone.ZoneName
                            HostName   = [string]$record.HostName
                            RecordType = [string]$record.RecordType
                            TimeToLive = [string]$record.TimeToLive
                            RecordData = $recordDataValue
                        }
                    }
                }
            }

            [PSCustomObject]@{
                ZonasJson = ($zonas | ConvertTo-Json -Depth 6)
                RegistrosJson = ($allRecords | ConvertTo-Json -Depth 6)
                EncaminhadoresJson = ($forwarders | ConvertTo-Json -Depth 6)
            }
        ";
    }
}
