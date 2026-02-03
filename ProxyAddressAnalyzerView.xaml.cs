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
    public partial class ProxyAddressAnalyzerView : UserControl
    {
        private readonly PowerShellService _powerShellService;
        private const string _moduleName = "ProxyAddressAnalyzer";

        public ProxyAddressAnalyzerView()
        {
            InitializeComponent();
            _powerShellService = new PowerShellService();
        }

        private async void RunProxyCollection(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button != null) button.IsEnabled = false;
            
            ProgressText.Visibility = Visibility.Visible;
            StatusText.Text = "⏳ Coletando informações de ProxyAddresses...";

            string scopeAttribute = ScopeAttributeBox.Text;
            string scopeValue = ScopeValueBox.Text;

            if (string.IsNullOrWhiteSpace(scopeAttribute) || string.IsNullOrWhiteSpace(scopeValue))
            {
                StatusText.Text = "⚠️ Por favor, preencha os campos de escopo para a busca filtrada.";
                ProgressText.Visibility = Visibility.Collapsed;
                if (button != null) button.IsEnabled = true;
                return;
            }

            LogService.Write(_moduleName, $"Iniciando coleta com critério: {scopeAttribute} = '{scopeValue}'.");

            try
            {
                // A lógica do script PowerShell permanece a mesma
                string scriptText = @"
                    param([string]$AttributeName, [string]$AttributeValue)
                    Import-Module ActiveDirectory -ErrorAction SilentlyContinue; if (-not (Get-Module ActiveDirectory)) { throw 'Módulo ActiveDirectory não encontrado.' }
                    $filter = ""($AttributeName -eq '$AttributeValue') -and (ProxyAddresses -like '*')""
                    
                    $users = Get-ADUser -Filter $filter -Properties ProxyAddresses, UserPrincipalName, SamAccountName
                    if (-not $users) { return }

                    $flatList = @()
                    foreach ($user in $users) {
                        foreach ($address in $user.ProxyAddresses) {
                            $isPrimary = if ($address -clike 'SMTP:*') { $true } else { $false }
                            $flatList += [PSCustomObject]@{
                                UserPrincipalName = $user.UserPrincipalName
                                SamAccountName    = $user.SamAccountName
                                ProxyAddress      = $address.ToString()
                                IsPrimarySmtp     = $isPrimary
                            }
                        }
                    }
                    return $flatList
                ";
                var scriptParameters = new Dictionary<string, object> { { "AttributeName", scopeAttribute }, { "AttributeValue", scopeValue } };
                
                var results = await _powerShellService.ExecuteScriptAsync(scriptText, scriptParameters);
                
                ProxyGrid.ItemsSource = results;
                AnimateGrid(ProxyGrid);
                
                string message = $"✅ Coleta concluída. {results.Count} endereços de proxy encontrados para os usuários no escopo.";
                StatusText.Text = message;
                LogService.Write(_moduleName, message);
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
            if (!(ProxyGrid.ItemsSource is IEnumerable<dynamic> data) || !data.Any()) { StatusText.Text = "⚠️ Não há dados para exportar."; return; }
            var saveDialog = new SaveFileDialog { FileName = $"ProxyAddresses_Escopo_{DateTime.Now:yyyyMMdd_HHmmss}.csv", Filter = "CSV Files (*.csv)|*.csv" };
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
            if (!(ProxyGrid.ItemsSource is IEnumerable<dynamic> data) || !data.Any()) { StatusText.Text = "⚠️ Não há dados para exportar."; return; }
            var saveDialog = new SaveFileDialog { FileName = $"ProxyAddresses_Escopo_{DateTime.Now:yyyyMMdd_HHmmss}.xml", Filter = "XML Files (*.xml)|*.xml" };
            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    using (var writer = new XmlTextWriter(saveDialog.FileName, Encoding.UTF8))
                    {
                        writer.Formatting = Formatting.Indented;
                        writer.WriteStartDocument(); writer.WriteStartElement("ProxyAddresses_Escopo");
                        foreach (var item in data)
                        {
                            if (item is IDictionary<string, object> itemDict)
                            {
                                writer.WriteStartElement("Address");
                                foreach (var kvp in itemDict) writer.WriteElementString(kvp.Key.Replace(" ", "_"), kvp.Value?.ToString() ?? "");
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
            if (!(ProxyGrid.ItemsSource is IEnumerable<dynamic> data) || !data.Any()) { StatusText.Text = "⚠️ Não há dados para exportar."; return; }
            var saveDialog = new SaveFileDialog { FileName = $"ProxyAddresses_Escopo_{DateTime.Now:yyyyMMdd_HHmmss}.html", Filter = "HTML Files (*.html)|*.html" };
            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("<html><head><title>Relatório de ProxyAddresses</title><style>body{font-family:sans-serif}table{border-collapse:collapse;width:100%}td,th{border:1px solid #ddd;padding:8px}tr:nth-child(even){background-color:#f2f2f2}</style></head><body>");
                    sb.AppendLine("<h2>Relatório de ProxyAddresses (Escopo)</h2><table>");
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
            if (!(ProxyGrid.ItemsSource is IEnumerable<dynamic> data) || !data.Any()) { StatusText.Text = "⚠️ Não há dados para exportar."; return; }
            try
            {
                var dialog = new SqlConnectionDialog();
                string domainName = _powerShellService.GetDomainNetBiosName();
                dialog.SetSuggestedDatabase(domainName);
                if (dialog.ShowDialog() == true)
                {
                    var sqlManager = new SqlManagerService(dialog.ServerName, dialog.DatabaseName, dialog.ConnectionString);
                    sqlManager.EnsureDatabaseExists();
                    
                    string tableName = $"ProxyAddresses_Escopo_{DateTime.Now:yyyyMMdd_HHmmss}";
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