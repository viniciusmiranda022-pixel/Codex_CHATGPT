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
using System.Management.Automation;

namespace DirectoryAnalyzer.Views
{
    public partial class GpoAnalyzerView : UserControl
    {
        private readonly PowerShellService _powerShellService;
        private const string ModuleName = "GpoAnalyzer";
        private readonly ILogService _logService;

        public GpoAnalyzerView()
        {
            InitializeComponent();
            _powerShellService = new PowerShellService();
            _logService = LogService.CreateLogger(ModuleName);
            UpdateStatus("✔️ Pronto para iniciar a coleta.", "Pronto");
            SetBusyState(false);
        }

        private async void RunFullGpoCollection(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button != null) button.IsEnabled = false;
            string correlationId = LogService.CreateCorrelationId();

            SetBusyState(true);
            UpdateStatus("⏳ Coletando informações de GPOs. Isso pode demorar vários minutos...", "Executando...");
            _logService.Info("Iniciando coleta completa de GPOs.", correlationId);
            bool success = false;
            int? itemCount = null;
            int? errorCount = null;
            DashboardService.Instance.RecordModuleStart("GPO Analyzer");

            try
            {
                // A lógica de coleta de dados permanece a mesma
                string scriptText = @"
                    Import-Module GroupPolicy -ErrorAction SilentlyContinue
                    Import-Module ActiveDirectory -ErrorAction SilentlyContinue
                    if (-not (Get-Module GroupPolicy)) { throw 'Módulo GroupPolicy não encontrado.' }

                    $gpoResumo = @()
                    $gpoLinks = @()
                    $gpoDelegacoes = @()
                    $gpoSecurityFiltering = @()
                    $gpoWmiFilters = @()

                    $gpos = Get-GPO -All
                    if (-not $gpos) { return }

                    foreach ($gpo in $gpos) {
                        $guid = $gpo.Id.ToString()
                        $nome = $gpo.DisplayName
                        
                        $wmiNome = if($gpo.WmiFilter) { $gpo.WmiFilter.Name } else { 'Nenhum' }
                        $gpoWmiFilters += [pscustomobject]@{ GPO_Nome = $nome; GPO_GUID = $guid; WMIFilterNome = $wmiNome; WMIQuery = if($gpo.WmiFilter) { $gpo.WmiFilter.Query } else { 'N/A' } }
                        $gpoResumo += [pscustomobject]@{ Nome = $nome; GUID = $guid; Status = $gpo.GpoStatus.ToString(); CriadoEm = $gpo.CreationTime; ModificadoEm = $gpo.ModificationTime; FiltroWMINome = $wmiNome }

                        try {
                            $report = Get-GPOReport -Guid $guid -ReportType Xml -ErrorAction Stop
                            [xml]$xmlDoc = $report

                            if ($xmlDoc.GPO.LinksTo.SOMObject) {
                                foreach ($som in $xmlDoc.GPO.LinksTo.SOMObject) {
                                    $gpoLinks += [pscustomobject]@{ GPO_Nome = $nome; VinculadoEm_DN = $som.SOMPath; LinkHabilitado = $som.Enabled; LinkForcado = $som.NoOverride }
                                }
                            } elseif ($xmlDoc.GPO.LinksTo.SOMPath) {
                               $gpoLinks += [pscustomobject]@{ GPO_Nome = $nome; VinculadoEm_DN = $xmlDoc.GPO.LinksTo.SOMPath; LinkHabilitado = $xmlDoc.GPO.LinksTo.Enabled; LinkForcado = $xmlDoc.GPO.LinksTo.NoOverride }
                            }

                            $perms = Get-GPPermission -Guid $guid -All -ErrorAction Stop
                            foreach ($perm in $perms) {
                                $gpoDelegacoes += [pscustomobject]@{ GPO_Nome = $nome; Trustee = $perm.Trustee.Name; Permissao = $perm.Permission.ToString() }
                                if ($perm.Permission -eq 'GpoApply' -or $perm.Permission -eq 'GpoRead') {
                                    $gpoSecurityFiltering += [pscustomobject]@{ GPO_Nome = $nome; FiltroSeguranca = $perm.Trustee.Name; PermissaoFiltro = $perm.Permission.ToString() }
                                }
                            }
                        } catch { }
                    }
                    
                    [PSCustomObject]@{
                        Resumo = $gpoResumo;
                        Links = $gpoLinks;
                        Delegacoes = $gpoDelegacoes;
                        SecurityFiltering = $gpoSecurityFiltering;
                        WmiFilters = $gpoWmiFilters
                    }
                ";

                var results = await _powerShellService.ExecuteScriptAsync(scriptText);

                if (results.FirstOrDefault() is IDictionary<string, object> data)
                {
                    var resumo = data["Resumo"] as IEnumerable;
                    
                    ResumoGrid.ItemsSource = resumo?.Cast<object>();
                    LinksGrid.ItemsSource = (data["Links"] as IEnumerable)?.Cast<object>();
                    DelegacaoGrid.ItemsSource = (data["Delegacoes"] as IEnumerable)?.Cast<object>();
                    SecurityFilteringGrid.ItemsSource = (data["SecurityFiltering"] as IEnumerable)?.Cast<object>();
                    WmiFiltersGrid.ItemsSource = (data["WmiFilters"] as IEnumerable)?.Cast<object>();

                    AnimateGrid(ResumoGrid);
                    AnimateGrid(LinksGrid);
                    AnimateGrid(DelegacaoGrid);
                    AnimateGrid(SecurityFilteringGrid);
                    AnimateGrid(WmiFiltersGrid);
                    
                    int gpoCount = resumo?.Cast<object>().Count() ?? 0;
                    string finalMessage = $"✅ Coleta finalizada. {gpoCount} GPOs encontradas.";
                    UpdateStatus(finalMessage, "Concluído");
                    _logService.Info(finalMessage, correlationId);
                    success = true;
                    itemCount = gpoCount;
                    errorCount = 0;
                }
                else 
                { 
                    UpdateStatus("⚠️ Coleta concluída, mas nenhum dado foi retornado.", "Concluído");
                    _logService.Warn("Coleta concluída sem dados.", correlationId);
                    success = true;
                    itemCount = 0;
                    errorCount = 0;
                }
            }
            catch (Exception ex) 
            { 
                UpdateStatus("❌ Erro durante a coleta: " + ex.Message, "Erro - ver log");
                _logService.Error("ERRO na coleta de GPO: " + ex, correlationId);
                errorCount = 1;
            }
            finally
            {
                SetBusyState(false);
                if (button != null) button.IsEnabled = true;
                DashboardService.Instance.RecordModuleFinish("GPO Analyzer", success, itemCount, errorCount);
            }
        }

        // Animação de fade-in para as grades
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
            if (grid == null || !(grid.ItemsSource is IEnumerable data) || !data.Cast<object>().Any()) { UpdateStatus("⚠️ Nenhum dado para exportar.", "Pronto"); return; }

            var saveDialog = new SaveFileDialog { FileName = $"GPOs_{reportType}_{DateTime.Now:yyyyMMdd_HHmmss}.csv", Filter = "CSV Files (*.csv)|*.csv", Title = $"Salvar Relatório CSV de {reportType}" };
            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    _logService.Info($"Iniciando exportação CSV: {saveDialog.FileName}", correlationId);
                    var sb = new StringBuilder();
                    var firstItemDict = PSObjectToDictionary(data.Cast<object>().FirstOrDefault());
                    if (firstItemDict.Any()) sb.AppendLine(string.Join(";", firstItemDict.Keys));

                    foreach (var item in data)
                    {
                        var itemDict = PSObjectToDictionary(item);
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
            if (grid == null || !(grid.ItemsSource is IEnumerable data) || !data.Cast<object>().Any()) { UpdateStatus("⚠️ Nenhum dado para exportar.", "Pronto"); return; }

            string rootElementName = $"GPOs_{reportType.Replace(" ", "").Replace("(", "_").Replace(")", "")}";
            var saveDialog = new SaveFileDialog { FileName = $"{rootElementName}_{DateTime.Now:yyyyMMdd_HHmmss}.xml", Filter = "XML Files (*.xml)|*.xml", Title = $"Salvar Relatório XML de {reportType}" };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    _logService.Info($"Iniciando exportação XML: {saveDialog.FileName}", correlationId);
                    using (var writer = new XmlTextWriter(saveDialog.FileName, Encoding.UTF8))
                    {
                        writer.Formatting = Formatting.Indented;
                        writer.WriteStartDocument(); writer.WriteStartElement(rootElementName);
                        foreach (var item in data)
                        {
                            writer.WriteStartElement("Item");
                            var itemDict = PSObjectToDictionary(item);
                            foreach (var kvp in itemDict) writer.WriteElementString(kvp.Key.Replace(" ", "_"), kvp.Value?.ToString() ?? string.Empty);
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
            if (grid == null || !(grid.ItemsSource is IEnumerable data) || !data.Cast<object>().Any()) { UpdateStatus("⚠️ Nenhum dado para exportar.", "Pronto"); return; }

            var saveDialog = new SaveFileDialog { FileName = $"GPOs_{reportType}_{DateTime.Now:yyyyMMdd_HHmmss}.html", Filter = "HTML Files (*.html)|*.html", Title = $"Salvar Relatório HTML de {reportType}" };
            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    _logService.Info($"Iniciando exportação HTML: {saveDialog.FileName}", correlationId);
                    var sb = new StringBuilder();
                    sb.AppendLine($"<html><head><meta charset='UTF-8'><title>Relatório de {reportType}</title><style>body{{font-family:sans-serif}}table{{border-collapse:collapse;width:100%}}td,th{{border:1px solid #dddddd;text-align:left;padding:8px}}tr:nth-child(even){{background-color:#f2f2f2}}</style></head><body>");
                    sb.AppendLine($"<h2>Relatório de {reportType}</h2><table>");
                    var firstItemDict = PSObjectToDictionary(data.Cast<object>().FirstOrDefault());
                    if (firstItemDict.Any())
                    {
                        sb.Append("<tr>");
                        foreach (var key in firstItemDict.Keys) sb.Append($"<th>{System.Security.SecurityElement.Escape(key)}</th>");
                        sb.AppendLine("</tr>");
                    }
                    foreach (var item in data)
                    {
                        var itemDict = PSObjectToDictionary(item);
                        sb.Append("<tr>");
                        foreach (var value in itemDict.Values) sb.Append($"<td>{System.Security.SecurityElement.Escape(value?.ToString() ?? "")}</td>");
                        sb.AppendLine("</tr>");
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
            if (grid == null || !(grid.ItemsSource is IEnumerable data) || !data.Cast<object>().Any()) { UpdateStatus("⚠️ Nenhum dado para exportar.", "Pronto"); return; }
            
            var dataForSql = new List<dynamic>();
            foreach (var item in data) dataForSql.Add(PSObjectToDictionary(item));
            
            string tableNameReportType = reportType.Replace(" ", "").Replace("(", "_").Replace(")", "");
            
            try
            {
                var dialog = new SqlConnectionDialog();
                string domainName = _powerShellService.GetDomainNetBiosName();
                dialog.SetSuggestedDatabase(domainName);
                
                if (dialog.ShowDialog() == true)
                {
                    string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string tableNameWithTimestamp = $"GPOs_{tableNameReportType}_{timeStamp}";
                    
                    _logService.Info($"Iniciando exportação SQL para tabela '{tableNameWithTimestamp}'.", correlationId);
                    ExportService.ExportToSql(dataForSql, tableNameWithTimestamp, dialog.ConnectionString);
                    
                    string successMessage = $"✅ Exportação SQL concluída.\nBanco: {dialog.DatabaseName}\nTabela: {tableNameWithTimestamp}";
                    UpdateStatus(successMessage, "Concluído");
                    _logService.Info($"Exportação SQL para a tabela '{tableNameWithTimestamp}' concluída com sucesso.", correlationId);
                }
            }
            catch (Exception ex) 
            { 
                UpdateStatus("❌ Erro ao exportar para SQL: " + ex.Message, "Erro - ver log");
                _logService.Error("ERRO na exportação para SQL: " + ex, correlationId);
            }
        }
        
        private DataGrid GetSelectedGrid(out string reportType)
        {
            reportType = string.Empty;
            if (!(ReportTabs.SelectedItem is TabItem selectedTab)) return null;

            reportType = selectedTab.Header.ToString();
            return selectedTab.Content as DataGrid;
        }

        private IDictionary<string, object> PSObjectToDictionary(object psObject)
        {
            var dictionary = new Dictionary<string, object>();
            if (psObject is PSObject pso)
            {
                foreach (var prop in pso.Properties)
                {
                    dictionary[prop.Name] = prop.Value;
                }
            }
            else if (psObject is IDictionary<string, object> d)
            {
                return d;
            }
            return dictionary;
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
    }
}
