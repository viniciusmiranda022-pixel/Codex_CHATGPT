using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using DirectoryAnalyzer.Dialogs;
using DirectoryAnalyzer.Services;
using Microsoft.Win32;
using System.IO;
using System.Xml;

namespace DirectoryAnalyzer.Views
{
    public partial class LocalSecurityPolicyAnalyzerView : UserControl
    {
        private readonly PowerShellService _powerShellService;
        private const string ModuleName = "LocalSecurityPolicyAnalyzer";
        private readonly ILogService _logService;

        public LocalSecurityPolicyAnalyzerView()
        {
            InitializeComponent();
            _powerShellService = new PowerShellService();
            _logService = LogService.CreateLogger(ModuleName);
            UpdateStatus("✔️ Pronto para iniciar a coleta.", "Pronto");
        }

        private async void RunPolicyCollection(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button != null) button.IsEnabled = false;
            string correlationId = LogService.CreateCorrelationId();

            ProgressText.Visibility = Visibility.Visible;
            UpdateStatus("⏳ Coletando informações de políticas de segurança. Isso pode demorar...", "Executando...");

            string scopeAttribute = ScopeAttributeBox.Text;
            string scopeValue = ScopeValueBox.Text;

            if (string.IsNullOrWhiteSpace(scopeAttribute) || string.IsNullOrWhiteSpace(scopeValue))
            {
                UpdateStatus("⚠️ Por favor, preencha o Atributo de Escopo e o Valor do Atributo.", "Pronto");
                ProgressText.Visibility = Visibility.Collapsed;
                if (button != null) button.IsEnabled = true;
                return;
            }

            _logService.Info($"Iniciando coleta com critério: {scopeAttribute} = '{scopeValue}'.", correlationId);

            try
            {
                // A lógica do script PowerShell permanece a mesma
                string scriptText = @"
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
                                        $type = $matches[1]; $value = $matches[2].Trim('""'); $finalValue = $value; $translated = ''
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
                var scriptParameters = new Dictionary<string, object> { { "AttributeName", scopeAttribute }, { "AttributeValue", scopeValue } };
                
                var results = await _powerShellService.ExecuteScriptAsync(scriptText, scriptParameters);
                
                PolicyGrid.ItemsSource = results;
                AnimateGrid(PolicyGrid);

                var errorRows = results.Count(item => (item as IDictionary<string, object>)["PolicyArea"].ToString().Contains("Erro"));
                var successRows = results.Count - errorRows;
                UpdateStatus($"✅ Coleta concluída. {successRows} políticas encontradas com {errorRows} erros de acesso/conexão.", "Concluído");
                _logService.Info(StatusText.Text, correlationId);
            }
            catch (Exception ex)
            {
                UpdateStatus("❌ Erro durante a coleta: " + ex.Message, "Erro - ver log");
                _logService.Error("ERRO GERAL NA COLETA: " + ex, correlationId);
            }
            finally
            {
                ProgressText.Visibility = Visibility.Collapsed;
                if (button != null) button.IsEnabled = true;
                _logService.Info("Execução finalizada.", correlationId);
            }
        }
        
        private void AnimateGrid(UIElement grid)
        {
            if (grid == null) return;
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400));
            grid.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            string correlationId = LogService.CreateCorrelationId();
            if (!(PolicyGrid.ItemsSource is IEnumerable<dynamic> data) || !data.Any()) { UpdateStatus("⚠️ Não há dados para exportar.", "Pronto"); return; }
            var saveDialog = new SaveFileDialog { FileName = $"LocalSecurityPolicy_{DateTime.Now:yyyyMMdd_HHmmss}.csv", Filter = "CSV Files (*.csv)|*.csv" };
            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    _logService.Info($"Iniciando exportação CSV: {saveDialog.FileName}", correlationId);
                    var sb = new StringBuilder();
                    if (data.FirstOrDefault() is IDictionary<string, object> firstItem) sb.AppendLine(string.Join(";", firstItem.Keys));
                    foreach (var item in data)
                    {
                        if (item is IDictionary<string, object> itemDict)
                        {
                            var values = itemDict.Values.Select(v => v?.ToString()?.Replace(";", ","));
                            sb.AppendLine(string.Join(";", values));
                        }
                    }
                    File.WriteAllText(saveDialog.FileName, sb.ToString(), Encoding.UTF8);
                    UpdateStatus($"✅ Exportação CSV concluída: {saveDialog.FileName}", "Concluído");
                    _logService.Info("Exportação CSV concluída.", correlationId);
                }
                catch (Exception ex) { UpdateStatus("❌ Erro ao exportar para CSV: " + ex.Message, "Erro - ver log"); _logService.Error("Erro ao exportar para CSV: " + ex, correlationId); }
            }
        }

        private void ExportXml_Click(object sender, RoutedEventArgs e)
        {
            string correlationId = LogService.CreateCorrelationId();
            if (!(PolicyGrid.ItemsSource is IEnumerable<dynamic> data) || !data.Any()) { UpdateStatus("⚠️ Não há dados para exportar.", "Pronto"); return; }
            var saveDialog = new SaveFileDialog { FileName = $"LocalSecurityPolicy_{DateTime.Now:yyyyMMdd_HHmmss}.xml", Filter = "XML Files (*.xml)|*.xml" };
            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    _logService.Info($"Iniciando exportação XML: {saveDialog.FileName}", correlationId);
                    using (var writer = new XmlTextWriter(saveDialog.FileName, Encoding.UTF8))
                    {
                        writer.Formatting = Formatting.Indented;
                        writer.WriteStartDocument(); writer.WriteStartElement("LocalSecurityPolicy");
                        foreach (var item in data)
                        {
                            if (item is IDictionary<string, object> itemDict)
                            {
                                writer.WriteStartElement("Policy");
                                foreach (var kvp in itemDict) writer.WriteElementString(kvp.Key.Replace(" ", "_"), kvp.Value?.ToString() ?? "");
                                writer.WriteEndElement();
                            }
                        }
                        writer.WriteEndElement(); writer.WriteEndDocument();
                    }
                    UpdateStatus($"✅ Exportação XML concluída: {saveDialog.FileName}", "Concluído");
                    _logService.Info("Exportação XML concluída.", correlationId);
                }
                catch (Exception ex) { UpdateStatus("❌ Erro ao exportar para XML: " + ex.Message, "Erro - ver log"); _logService.Error("Erro ao exportar para XML: " + ex, correlationId); }
            }
        }

        private void ExportHtml_Click(object sender, RoutedEventArgs e)
        {
            string correlationId = LogService.CreateCorrelationId();
            if (!(PolicyGrid.ItemsSource is IEnumerable<dynamic> data) || !data.Any()) { UpdateStatus("⚠️ Não há dados para exportar.", "Pronto"); return; }
            var saveDialog = new SaveFileDialog { FileName = $"LocalSecurityPolicy_{DateTime.Now:yyyyMMdd_HHmmss}.html", Filter = "HTML Files (*.html)|*.html" };
            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    _logService.Info($"Iniciando exportação HTML: {saveDialog.FileName}", correlationId);
                    var sb = new StringBuilder();
                    sb.AppendLine("<html><head><title>Relatório de Política de Segurança Local</title><style>body{font-family:sans-serif}table{border-collapse:collapse;width:100%}td,th{border:1px solid #ddd;padding:8px}tr:nth-child(even){background-color:#f2f2f2}</style></head><body>");
                    sb.AppendLine("<h2>Relatório de Política de Segurança Local</h2><table>");
                    if (data.FirstOrDefault() is IDictionary<string, object> firstItem)
                    {
                        sb.Append("<tr>"); foreach (var key in firstItem.Keys) sb.Append($"<th>{System.Security.SecurityElement.Escape(key)}</th>"); sb.AppendLine("</tr>");
                    }
                    foreach (var item in data)
                    {
                        if (item is IDictionary<string, object> itemDict)
                        {
                            sb.Append("<tr>"); foreach (var value in itemDict.Values) sb.Append($"<td>{System.Security.SecurityElement.Escape(value?.ToString() ?? "")}</td>"); sb.AppendLine("</tr>");
                        }
                    }
                    sb.AppendLine("</table></body></html>");
                    File.WriteAllText(saveDialog.FileName, sb.ToString(), Encoding.UTF8);
                    UpdateStatus($"✅ Exportação HTML concluída: {saveDialog.FileName}", "Concluído");
                    _logService.Info("Exportação HTML concluída.", correlationId);
                }
                catch (Exception ex) { UpdateStatus("❌ Erro ao exportar para HTML: " + ex.Message, "Erro - ver log"); _logService.Error("Erro ao exportar para HTML: " + ex, correlationId); }
            }
        }

        private void ExportSql_Click(object sender, RoutedEventArgs e)
        {
            string correlationId = LogService.CreateCorrelationId();
            if (!(PolicyGrid.ItemsSource is IEnumerable<dynamic> data) || !data.Any()) { UpdateStatus("⚠️ Não há dados para exportar.", "Pronto"); return; }
            try
            {
                var dialog = new SqlConnectionDialog();
                string domainName = _powerShellService.GetDomainNetBiosName();
                dialog.SetSuggestedDatabase(domainName);
                if (dialog.ShowDialog() == true)
                {
                    var sqlManager = new SqlManagerService(dialog.ServerName, dialog.DatabaseName, dialog.ConnectionString);
                    sqlManager.EnsureDatabaseExists();
                    
                    string tableName = $"LocalSecurityPolicy_{DateTime.Now:yyyyMMdd_HHmmss}";
                    _logService.Info($"Iniciando exportação SQL para tabela '{tableName}'.", correlationId);
                    ExportService.ExportToSql(data, tableName, dialog.ConnectionString);

                    UpdateStatus($"✅ Exportação SQL concluída com sucesso.\nBanco: {dialog.DatabaseName}", "Concluído");
                    _logService.Info($"Exportação SQL para a tabela '{tableName}' concluída com sucesso.", correlationId);
                }
            }
            catch (Exception ex) 
            {
                UpdateStatus("❌ Erro ao exportar para SQL: " + ex.Message, "Erro - ver log");
                _logService.Error("ERRO na exportação para SQL: " + ex, correlationId);
            }
        }

        private void UpdateStatus(string message, string globalStatus)
        {
            StatusText.Text = message;
            StatusService.Instance.SetStatus(globalStatus);
        }
    }
}
