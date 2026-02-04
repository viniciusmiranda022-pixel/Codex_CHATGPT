using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
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
 codex/transform-product-to-agent-only-architecture-xaez7h
        private readonly ModuleCollectionService _collectionService;

        private readonly BrokerJobService _brokerJobService;
 main
        private const string ModuleName = "InstalledServicesAnalyzer";
        private readonly ILogService _logService;

        public InstalledServicesAnalyzerView()
        {
            InitializeComponent();
            var settings = BrokerClientSettingsLoader.Load(BrokerClientSettingsStore.ResolvePath());
 codex/transform-product-to-agent-only-architecture-xaez7h
            _collectionService = new ModuleCollectionService(new BrokerJobService(settings));

            _brokerJobService = new BrokerJobService(settings);
 main
            _logService = LogService.CreateLogger(ModuleName);
            UpdateStatus("✔️ Pronto para iniciar a coleta.", "Pronto");
            SetBusyState(false);
        }

        private async void RunServicesCollection(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button != null) button.IsEnabled = false;
            string correlationId = LogService.CreateCorrelationId();
            bool success = false;
            int? itemCount = null;
            int? errorCount = null;

            SetBusyState(true);
            UpdateStatus("⏳ Coletando informações de serviços instalados...", "Executando...");

            string scopeAttribute = ScopeAttributeBox.Text;
            string scopeValue = ScopeValueBox.Text;

            if (string.IsNullOrWhiteSpace(scopeAttribute) || string.IsNullOrWhiteSpace(scopeValue))
            {
                UpdateStatus("⚠️ Por favor, preencha o Atributo de Escopo e o Valor do Atributo.", "Pronto");
                SetBusyState(false);
                if (button != null) button.IsEnabled = true;
                return;
            }

            _logService.Info($"Iniciando coleta com critério: {scopeAttribute} = '{scopeValue}'.", correlationId);
            DashboardService.Instance.RecordModuleStart("Service Account Analyzer");

            try
            {
 codex/transform-product-to-agent-only-architecture-xaez7h
                var moduleResult = await _collectionService.RunInstalledServicesAsync(
                    scopeAttribute,
                    scopeValue,

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
                var scriptParameters = new Dictionary<string, string>
                {
                    { "AttributeName", scopeAttribute },
                    { "AttributeValue", scopeValue }
                };

                var moduleResult = await _brokerJobService.RunPowerShellScriptAsync(
                    ModuleName,
                    scriptText,
                    scriptParameters,
 main
                    Environment.UserName,
                    CancellationToken.None);

                var displayItems = ResultItemAdapter.ToDisplayItems(moduleResult?.Items);
                var resultsList = displayItems.Cast<IDictionary<string, object>>().ToList();

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

                string finalMessage = $"✅ Coleta concluída. {resultsList.Count} serviços totais | {domainAccounts.Count} com contas de domínio | {localAccounts.Count} com contas locais.";
                UpdateStatus(finalMessage, "Concluído");
                _logService.Info(finalMessage, correlationId);
                success = true;
                itemCount = resultsList.Count;
                errorCount = moduleResult?.Errors?.Count ?? 0;
            }
            catch (Exception ex)
            {
                UpdateStatus("❌ Erro durante a coleta: " + ex.Message, "Erro - ver log");
                _logService.Error("ERRO GERAL NA COLETA: " + ex, correlationId);
                errorCount = 1;
            }
            finally
            {
                SetBusyState(false);
                if (button != null) button.IsEnabled = true;
                _logService.Info("Execução finalizada.", correlationId);
                DashboardService.Instance.RecordModuleFinish("Service Account Analyzer", success, itemCount, errorCount);
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
            var grid = GetSelectedGrid(out string reportType);
            if (grid == null || !(grid.ItemsSource is IEnumerable data) || !data.Cast<object>().Any()) { UpdateStatus("⚠️ Selecione uma aba com dados para exportar.", "Pronto"); return; }
            var dataList = data.Cast<IDictionary<string, object>>().ToList();
            if(!dataList.Any()) { UpdateStatus("⚠️ Não há dados na aba selecionada para exportar.", "Pronto"); return; }

            var saveDialog = new SaveFileDialog { FileName = $"Servicos_{reportType}_{DateTime.Now:yyyyMMdd_HHmmss}.csv", Filter = "CSV Files (*.csv)|*.csv" };
            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    _logService.Info($"Iniciando exportação CSV: {saveDialog.FileName}", correlationId);
                    var sb = new StringBuilder();
                    var firstItem = dataList.FirstOrDefault();
                    if (firstItem != null) sb.AppendLine(string.Join(";", firstItem.Keys));
                    foreach (var itemDict in dataList)
                    {
                        var values = itemDict.Values.Select(v => v?.ToString()?.Replace(";", ","));
                        sb.AppendLine(string.Join(";", values));
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
            var grid = GetSelectedGrid(out string reportType);
            if (grid == null || !(grid.ItemsSource is IEnumerable data) || !data.Cast<object>().Any()) { UpdateStatus("⚠️ Selecione uma aba com dados para exportar.", "Pronto"); return; }
            var dataList = data.Cast<IDictionary<string, object>>().ToList();
            if (!dataList.Any()) { UpdateStatus("⚠️ Não há dados na aba selecionada para exportar.", "Pronto"); return; }

            string rootElementName = $"Servicos_{reportType}";
            var saveDialog = new SaveFileDialog { FileName = $"{rootElementName}_{DateTime.Now:yyyyMMdd_HHmmss}.xml", Filter = "XML Files (*.xml)|*.xml" };
            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    _logService.Info($"Iniciando exportação XML: {saveDialog.FileName}", correlationId);
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
                    UpdateStatus($"✅ Exportação XML concluída: {saveDialog.FileName}", "Concluído");
                    _logService.Info("Exportação XML concluída.", correlationId);
                }
                catch (Exception ex) { UpdateStatus("❌ Erro ao exportar para XML: " + ex.Message, "Erro - ver log"); _logService.Error("Erro ao exportar para XML: " + ex, correlationId); }
            }
        }

        private void ExportHtml_Click(object sender, RoutedEventArgs e)
        {
            string correlationId = LogService.CreateCorrelationId();
            var grid = GetSelectedGrid(out string reportType);
            if (grid == null || !(grid.ItemsSource is IEnumerable data) || !data.Cast<object>().Any()) { UpdateStatus("⚠️ Selecione uma aba com dados para exportar.", "Pronto"); return; }
            var dataList = data.Cast<IDictionary<string, object>>().ToList();
            if (!dataList.Any()) { UpdateStatus("⚠️ Não há dados na aba selecionada para exportar.", "Pronto"); return; }

            var saveDialog = new SaveFileDialog { FileName = $"Servicos_{reportType}_{DateTime.Now:yyyyMMdd_HHmmss}.html", Filter = "HTML Files (*.html)|*.html" };
            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    _logService.Info($"Iniciando exportação HTML: {saveDialog.FileName}", correlationId);
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
                    UpdateStatus($"✅ Exportação HTML concluída: {saveDialog.FileName}", "Concluído");
                    _logService.Info("Exportação HTML concluída.", correlationId);
                }
                catch (Exception ex) { UpdateStatus("❌ Erro ao exportar para HTML: " + ex.Message, "Erro - ver log"); _logService.Error("Erro ao exportar para HTML: " + ex, correlationId); }
            }
        }

        private void ExportSql_Click(object sender, RoutedEventArgs e)
        {
            string correlationId = LogService.CreateCorrelationId();
            var grid = GetSelectedGrid(out string reportType);
            if (grid == null || !(grid.ItemsSource is IEnumerable<dynamic> data) || !data.Any()) { UpdateStatus("⚠️ Selecione uma aba com dados para exportar.", "Pronto"); return; }
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

        private void SetBusyState(bool isBusy)
        {
            ProgressBar.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
            ProgressText.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
            ExecuteButton.IsEnabled = !isBusy;
            ExportCsvButton.IsEnabled = !isBusy;
            ExportXmlButton.IsEnabled = !isBusy;
            ExportHtmlButton.IsEnabled = !isBusy;
            ExportSqlButton.IsEnabled = !isBusy;
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
