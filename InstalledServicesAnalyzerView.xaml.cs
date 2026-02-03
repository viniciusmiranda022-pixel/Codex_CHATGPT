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
    public partial class InstalledServicesAnalyzerView : UserControl
    {
        private readonly PowerShellService _powerShellService;
        private const string _moduleName = "InstalledServicesAnalyzer";

        public InstalledServicesAnalyzerView()
        {
            InitializeComponent();
            _powerShellService = new PowerShellService();
        }

        private async void RunServicesCollection(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button != null) button.IsEnabled = false;

            ProgressText.Visibility = Visibility.Visible;
            StatusText.Text = "⏳ Coletando informações de serviços instalados...";

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
                // A lógica do script PowerShell permanece a mesma
                string scriptText = @"
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
                var scriptParameters = new Dictionary<string, object> { { "AttributeName", scopeAttribute }, { "AttributeValue", scopeValue } };
                
                var results = await _powerShellService.ExecuteScriptAsync(scriptText, scriptParameters);

                var resultsList = results.Cast<IDictionary<string, object>>().ToList();
                
                AllServicesGrid.ItemsSource = resultsList;
                AnimateGrid(AllServicesGrid);

                var systemAccounts = new List<string> { "LocalSystem", "NT AUTHORITY\\LocalService", "NT AUTHORITY\\NetworkService" };
                
                var serviceAccounts = resultsList
                    .Where(s => s["ContaDeServico"] != null && 
                                !systemAccounts.Any(sa => sa.Equals(s["ContaDeServico"].ToString(), StringComparison.OrdinalIgnoreCase)) &&
                                !s["ContaDeServico"].ToString().StartsWith("NT SERVICE\\", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                
                var domainAccounts = serviceAccounts.Where(s => s["ContaDeServico"].ToString().Contains("\\") || s["ContaDeServico"].ToString().Contains("@")).ToList();
                DomainAccountsGrid.ItemsSource = domainAccounts;
                AnimateGrid(DomainAccountsGrid);
                
                var localAccounts = serviceAccounts.Where(s => !s["ContaDeServico"].ToString().Contains("\\") && !s["ContaDeServico"].ToString().Contains("@")).ToList();
                LocalAccountsGrid.ItemsSource = localAccounts;
                AnimateGrid(LocalAccountsGrid);

                string finalMessage = $"✅ Coleta concluída. {results.Count} serviços totais | {domainAccounts.Count} com contas de domínio | {localAccounts.Count} com contas locais.";
                StatusText.Text = finalMessage;
                LogService.Write(_moduleName, finalMessage);
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
            var grid = GetSelectedGrid(out string reportType);
            if (grid == null || !(grid.ItemsSource is IEnumerable data) || !data.Cast<object>().Any()) { StatusText.Text = "⚠️ Selecione uma aba com dados para exportar."; return; }
            var dataList = data.Cast<IDictionary<string, object>>().ToList();
            if(!dataList.Any()) { StatusText.Text = "⚠️ Não há dados na aba selecionada para exportar."; return; }

            var saveDialog = new SaveFileDialog { FileName = $"Servicos_{reportType}_{DateTime.Now:yyyyMMdd_HHmmss}.csv", Filter = "CSV Files (*.csv)|*.csv" };
            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    var sb = new StringBuilder();
                    var firstItem = dataList.FirstOrDefault();
                    if (firstItem != null) sb.AppendLine(string.Join(";", firstItem.Keys));
                    foreach (var itemDict in dataList)
                    {
                        var values = itemDict.Values.Select(v => v?.ToString()?.Replace(";", ","));
                        sb.AppendLine(string.Join(";", values));
                    }
                    File.WriteAllText(saveDialog.FileName, sb.ToString(), Encoding.UTF8);
                    StatusText.Text = $"✅ Exportação CSV concluída: {saveDialog.FileName}";
                }
                catch (Exception ex) { StatusText.Text = "❌ Erro ao exportar para CSV: " + ex.Message; }
            }
        }

        private void ExportXml_Click(object sender, RoutedEventArgs e)
        {
            var grid = GetSelectedGrid(out string reportType);
            if (grid == null || !(grid.ItemsSource is IEnumerable data) || !data.Cast<object>().Any()) { StatusText.Text = "⚠️ Selecione uma aba com dados para exportar."; return; }
            var dataList = data.Cast<IDictionary<string, object>>().ToList();
            if (!dataList.Any()) { StatusText.Text = "⚠️ Não há dados na aba selecionada para exportar."; return; }

            string rootElementName = $"Servicos_{reportType}";
            var saveDialog = new SaveFileDialog { FileName = $"{rootElementName}_{DateTime.Now:yyyyMMdd_HHmmss}.xml", Filter = "XML Files (*.xml)|*.xml" };
            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    using (var writer = new XmlTextWriter(saveDialog.FileName, Encoding.UTF8))
                    {
                        writer.Formatting = Formatting.Indented;
                        writer.WriteStartDocument(); writer.WriteStartElement(rootElementName);
                        foreach (var itemDict in dataList)
                        {
                            writer.WriteStartElement("Servico");
                            foreach (var kvp in itemDict) writer.WriteElementString(kvp.Key.Replace(" ", "_"), kvp.Value?.ToString() ?? "");
                            writer.WriteEndElement();
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
            var grid = GetSelectedGrid(out string reportType);
            if (grid == null || !(grid.ItemsSource is IEnumerable data) || !data.Cast<object>().Any()) { StatusText.Text = "⚠️ Selecione uma aba com dados para exportar."; return; }
            var dataList = data.Cast<IDictionary<string, object>>().ToList();
            if (!dataList.Any()) { StatusText.Text = "⚠️ Não há dados na aba selecionada para exportar."; return; }

            var saveDialog = new SaveFileDialog { FileName = $"Servicos_{reportType}_{DateTime.Now:yyyyMMdd_HHmmss}.html", Filter = "HTML Files (*.html)|*.html" };
            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"<html><head><title>Relatório de {reportType}</title><style>body{{font-family:sans-serif}}table{{border-collapse:collapse;width:100%}}td,th{{border:1px solid #ddd;padding:8px}}tr:nth-child(even){{background-color:#f2f2f2}}</style></head><body>");
                    sb.AppendLine($"<h2>Relatório de {reportType}</h2><table>");
                    var firstItem = dataList.FirstOrDefault();
                    if (firstItem != null)
                    {
                        sb.Append("<tr>"); foreach (var key in firstItem.Keys) sb.Append($"<th>{System.Security.SecurityElement.Escape(key)}</th>"); sb.AppendLine("</tr>");
                    }
                    foreach (var itemDict in dataList)
                    {
                        sb.Append("<tr>"); foreach (var value in itemDict.Values) sb.Append($"<td>{System.Security.SecurityElement.Escape(value?.ToString() ?? "")}</td>"); sb.AppendLine("</tr>");
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
            var grid = GetSelectedGrid(out string reportType);
            if (grid == null || !(grid.ItemsSource is IEnumerable<dynamic> data) || !data.Any()) { StatusText.Text = "⚠️ Selecione uma aba com dados para exportar."; return; }
            try
            {
                var dialog = new SqlConnectionDialog();
                string domainName = _powerShellService.GetDomainNetBiosName();
                dialog.SetSuggestedDatabase(domainName);
                if (dialog.ShowDialog() == true)
                {
                    var sqlManager = new SqlManagerService(dialog.ServerName, dialog.DatabaseName, dialog.ConnectionString);
                    sqlManager.EnsureDatabaseExists();
                    
                    string tableName = $"Servicos_{reportType}_{DateTime.Now:yyyyMMdd_HHmmss}";
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

        private DataGrid GetSelectedGrid(out string reportType)
        {
            reportType = "Desconhecido";
            if (!(ReportTabs.SelectedItem is TabItem selectedTab)) return null;

            reportType = selectedTab.Header.ToString().Replace(" ", "_");
            return selectedTab.Content as DataGrid;
        }
    }
}