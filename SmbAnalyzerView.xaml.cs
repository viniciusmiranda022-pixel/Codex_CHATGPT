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
    public partial class SmbAnalyzerView : UserControl
    {
        private readonly PowerShellService _powerShellService;
        private const string _moduleName = "SmbSharesAnalyzer";

        public SmbAnalyzerView()
        {
            InitializeComponent();
            _powerShellService = new PowerShellService();
        }

        private async void RunSmbCollection(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button != null) button.IsEnabled = false;
            
            ProgressText.Visibility = Visibility.Visible;
            StatusText.Text = "⏳ Coletando informações de compartilhamentos SMB. Isso pode demorar...";

            string scopeAttribute = ScopeAttributeBox.Text;
            string scopeValue = ScopeValueBox.Text;

            if (string.IsNullOrWhiteSpace(scopeAttribute) || string.IsNullOrWhiteSpace(scopeValue))
            {
                StatusText.Text = "⚠️ Por favor, preencha o Atributo de Escopo e o Valor do Atributo.";
                ProgressText.Visibility = Visibility.Collapsed;
                if (button != null) button.IsEnabled = true;
                return;
            }

            LogService.Write(_moduleName, $"Iniciando coleta com critério: {scopeAttribute} = '{scopeValue}'.");
            
            try
            {
                string scriptText = @"
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
                var scriptParameters = new Dictionary<string, object> { { "AttributeName", scopeAttribute }, { "AttributeValue", scopeValue } };
                
                var results = await _powerShellService.ExecuteScriptAsync(scriptText, scriptParameters);
                
                SmbGrid.ItemsSource = results;
                AnimateGrid(SmbGrid);
                
                if (!results.Any())
                {
                    StatusText.Text = "✅ Coleta concluída. Nenhum compartilhamento encontrado para os critérios especificados.";
                }
                else
                {
                    var errorRows = results.Count(item => (item as IDictionary<string, object>)["IdentityReference"].ToString().Contains("ERRO"));
                    var successRows = results.Count - errorRows;
                    StatusText.Text = $"✅ Coleta concluída. {successRows} permissões encontradas com {errorRows} erros de acesso/conexão.";
                }
                LogService.Write(_moduleName, StatusText.Text);
            }
            catch (Exception ex)
            {
                StatusText.Text = "❌ Erro durante a coleta: " + ex.Message;
                LogService.Write(_moduleName, "ERRO GERAL NA COLETA: " + ex.ToString());
            }
            finally
            {
                ProgressText.Visibility = Visibility.Collapsed;
                if (button != null) button.IsEnabled = true;
                LogService.Write(_moduleName, "Execução finalizada.");
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
            if (!(SmbGrid.ItemsSource is IEnumerable<dynamic> data) || !data.Any()) { StatusText.Text = "⚠️ Não há dados para exportar."; return; }
            var saveDialog = new SaveFileDialog { FileName = $"SMB_Permissoes_{DateTime.Now:yyyyMMdd_HHmmss}.csv", Filter = "CSV Files (*.csv)|*.csv", Title = "Salvar Relatório CSV" };
            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    var sb = new StringBuilder();
                    if (data.FirstOrDefault() is IDictionary<string, object> firstItem)
                    {
                        sb.AppendLine(string.Join(";", firstItem.Keys));
                    }
                    foreach (var item in data)
                    {
                        if (item is IDictionary<string, object> itemDict)
                        {
                            var values = itemDict.Values.Select(v => v?.ToString()?.Replace(";", ","));
                            sb.AppendLine(string.Join(";", values));
                        }
                    }
                    File.WriteAllText(saveDialog.FileName, sb.ToString(), Encoding.UTF8);
                    StatusText.Text = $"✅ Exportação CSV concluída: {saveDialog.FileName}";
                }
                catch (Exception ex) { StatusText.Text = "❌ Erro ao exportar para CSV: " + ex.Message; }
            }
        }

        private void ExportXml_Click(object sender, RoutedEventArgs e)
        {
            if (!(SmbGrid.ItemsSource is IEnumerable<dynamic> data) || !data.Any()) { StatusText.Text = "⚠️ Não há dados para exportar."; return; }
            var saveDialog = new SaveFileDialog { FileName = $"SMB_Permissoes_{DateTime.Now:yyyyMMdd_HHmmss}.xml", Filter = "XML Files (*.xml)|*.xml", Title = "Salvar Relatório XML" };
            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    using (var writer = new XmlTextWriter(saveDialog.FileName, Encoding.UTF8))
                    {
                        writer.Formatting = Formatting.Indented;
                        writer.WriteStartDocument(); writer.WriteStartElement("SMB_Permissoes");
                        foreach (var item in data)
                        {
                            if (item is IDictionary<string, object> itemDict)
                            {
                                writer.WriteStartElement("Permissao");
                                foreach (var kvp in itemDict) writer.WriteElementString(kvp.Key.Replace(" ", "_"), kvp.Value?.ToString() ?? string.Empty);
                                writer.WriteEndElement();
                            }
                        }
                        writer.WriteEndElement(); writer.WriteEndDocument();
                    }
                    StatusText.Text = $"✅ Exportação XML concluída: {saveDialog.FileName}";
                }
                catch (Exception ex) { StatusText.Text = "❌ Erro ao exportar para XML: " + ex.Message; }
            }
        }

        private void ExportHtml_Click(object sender, RoutedEventArgs e)
        {
            if (!(SmbGrid.ItemsSource is IEnumerable<dynamic> data) || !data.Any()) { StatusText.Text = "⚠️ Não há dados para exportar."; return; }
            var saveDialog = new SaveFileDialog { FileName = $"SMB_Permissoes_{DateTime.Now:yyyyMMdd_HHmmss}.html", Filter = "HTML Files (*.html)|*.html", Title = "Salvar Relatório HTML" };
            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("<html><head><meta charset='UTF-8'><title>Relatório de Permissões SMB</title><style>body{font-family:sans-serif}table{border-collapse:collapse;width:100%}td,th{border:1px solid #dddddd;text-align:left;padding:8px}tr:nth-child(even){background-color:#f2f2f2}</style></head><body>");
                    sb.AppendLine("<h2>Relatório de Permissões SMB</h2><table>");
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
                    StatusText.Text = $"✅ Exportação HTML concluída: {saveDialog.FileName}";
                }
                catch (Exception ex) { StatusText.Text = "❌ Erro ao exportar para HTML: " + ex.Message; }
            }
        }

        private void ExportSql_Click(object sender, RoutedEventArgs e)
        {
            if (!(SmbGrid.ItemsSource is IEnumerable<dynamic> data) || !data.Any()) { StatusText.Text = "⚠️ Não há dados para exportar."; return; }
            try
            {
                var dialog = new SqlConnectionDialog();
                string domainName = _powerShellService.GetDomainNetBiosName();
                dialog.SetSuggestedDatabase(domainName);
                
                if (dialog.ShowDialog() == true)
                {
                    var sqlManager = new SqlManagerService(dialog.ServerName, dialog.DatabaseName, dialog.ConnectionString);
                    sqlManager.EnsureDatabaseExists();
                    
                    string tableName = $"SMB_Permissoes_{DateTime.Now:yyyyMMdd_HHmmss}";
                    LogService.Write(_moduleName, $"Iniciando exportação SQL para tabela '{tableName}'.");
                    ExportService.ExportToSql(data, tableName, dialog.ConnectionString);
                    
                    StatusText.Text = $"✅ Exportação SQL concluída com sucesso.\nBanco: {dialog.DatabaseName}";
                    LogService.Write(_moduleName, $"Exportação SQL para a tabela '{tableName}' concluída com sucesso.");
                }
            }
            catch (Exception ex) 
            {
                StatusText.Text = "❌ Erro ao exportar para SQL: " + ex.Message;
                LogService.Write(_moduleName, "ERRO na exportação para SQL: " + ex.ToString());
            }
        }
    }
}