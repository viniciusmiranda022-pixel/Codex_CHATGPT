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
    public partial class DnsAnalyzerView : UserControl
    {
        private readonly PowerShellService _powerShellService;
        // ADICIONADO: Constante para o nome do módulo de log
        private const string _moduleName = "DnsAnalyzer";

        public DnsAnalyzerView()
        {
            InitializeComponent();
            _powerShellService = new PowerShellService();
        }

        private async void RunFullDnsCollection(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button != null) button.IsEnabled = false;

            ProgressText.Visibility = Visibility.Visible;
            StatusText.Text = "⏳ Coletando informações de DNS...";
            LogService.Write(_moduleName, "Iniciando coleta completa de DNS.");

            try
            {
                string scriptText = @"
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
                        Zonas = $zonas
                        Registros = $allRecords
                        Encaminhadores = $forwarders
                    }
                ";

                var results = await _powerShellService.ExecuteScriptAsync(scriptText);

                if (results.FirstOrDefault() is IDictionary<string, object> data)
                {
                    var zonas = data["Zonas"] as IEnumerable;
                    var registros = data["Registros"] as IEnumerable;
                    var forwarders = data["Encaminhadores"] as IEnumerable;

                    ZonasGrid.ItemsSource = zonas?.Cast<object>();
                    RegistrosGrid.ItemsSource = registros?.Cast<object>();
                    ForwardersGrid.ItemsSource = forwarders?.Cast<object>();

                    AnimateGrid(ZonasGrid);
                    AnimateGrid(RegistrosGrid);
                    AnimateGrid(ForwardersGrid);

                    int zonasCount = zonas?.Cast<object>().Count() ?? 0;
                    int registrosCount = registros?.Cast<object>().Count() ?? 0;
                    int forwardersCount = forwarders?.Cast<object>().Count() ?? 0;
                    
                    string finalMessage = $"✅ Coleta finalizada. {zonasCount} zonas, {registrosCount} registros, {forwardersCount} encaminhadores.";
                    StatusText.Text = finalMessage;
                    LogService.Write(_moduleName, finalMessage);
                }
                else
                {
                    StatusText.Text = "⚠️ Coleta concluída, mas nenhum dado foi retornado.";
                    LogService.Write(_moduleName, "Coleta concluída sem dados.");
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "❌ Erro durante a coleta: " + ex.Message;
                LogService.Write(_moduleName, "ERRO na coleta de DNS: " + ex.ToString());
            }
            finally
            {
                ProgressText.Visibility = Visibility.Collapsed;
                if (button != null) button.IsEnabled = true;
            }
        }

        private void AnimateGrid(UIElement grid)
        {
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
            grid.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var grid = GetSelectedGrid(out string reportType);
            if (grid == null || !(grid.ItemsSource is IEnumerable data) || !data.Cast<object>().Any()) { StatusText.Text = "⚠️ Nenhum dado para exportar."; return; }

            var saveDialog = new SaveFileDialog { FileName = $"DNS_{reportType}_{DateTime.Now:yyyyMMdd_HHmmss}.csv", Filter = "CSV Files (*.csv)|*.csv" };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    var sb = new StringBuilder();
                    var firstItem = PSObjectToDictionary(data.Cast<object>().FirstOrDefault());
                    sb.AppendLine(string.Join(";", firstItem.Keys));

                    foreach (var item in data)
                    {
                        var dict = PSObjectToDictionary(item);
                        sb.AppendLine(string.Join(";", dict.Values.Select(v => v?.ToString()?.Replace(";", ","))));
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
            if (grid == null || !(grid.ItemsSource is IEnumerable data) || !data.Cast<object>().Any()) { StatusText.Text = "⚠️ Nenhum dado para exportar."; return; }

            var saveDialog = new SaveFileDialog { FileName = $"DNS_{reportType}_{DateTime.Now:yyyyMMdd_HHmmss}.xml", Filter = "XML Files (*.xml)|*.xml" };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    using (var writer = new XmlTextWriter(saveDialog.FileName, Encoding.UTF8))
                    {
                        writer.Formatting = Formatting.Indented;
                        writer.WriteStartDocument();
                        writer.WriteStartElement($"DNS_{reportType}");

                        foreach (var item in data)
                        {
                            var dict = PSObjectToDictionary(item);
                            writer.WriteStartElement("Item");
                            foreach (var kvp in dict)
                                writer.WriteElementString(kvp.Key.Replace(" ", "_"), kvp.Value?.ToString() ?? string.Empty);
                            writer.WriteEndElement();
                        }

                        writer.WriteEndElement();
                        writer.WriteEndDocument();
                    }
                    StatusText.Text = $"✅ Exportação XML concluída: {saveDialog.FileName}";
                }
                catch (Exception ex) { StatusText.Text = "❌ Erro ao exportar para XML: " + ex.Message; }
            }
        }

        private void ExportHtml_Click(object sender, RoutedEventArgs e)
        {
            var grid = GetSelectedGrid(out string reportType);
            if (grid == null || !(grid.ItemsSource is IEnumerable data) || !data.Cast<object>().Any()) { StatusText.Text = "⚠️ Nenhum dado para exportar."; return; }

            var saveDialog = new SaveFileDialog { FileName = $"DNS_{reportType}_{DateTime.Now:yyyyMMdd_HHmmss}.html", Filter = "HTML Files (*.html)|*.html" };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"<html><head><meta charset='UTF-8'><title>Relatório de {reportType}</title><style>body{{font-family:sans-serif}}table{{border-collapse:collapse;width:100%}}td,th{{border:1px solid #ddd;padding:8px}}tr:nth-child(even){{background-color:#f2f2f2}}</style></head><body>");
                    sb.AppendLine($"<h2>Relatório de {reportType}</h2><table>");

                    var firstItem = PSObjectToDictionary(data.Cast<object>().FirstOrDefault());
                    sb.Append("<tr>");
                    foreach (var key in firstItem.Keys) sb.Append($"<th>{System.Security.SecurityElement.Escape(key)}</th>");
                    sb.AppendLine("</tr>");

                    foreach (var item in data)
                    {
                        var dict = PSObjectToDictionary(item);
                        sb.Append("<tr>");
                        foreach (var val in dict.Values)
                            sb.Append($"<td>{System.Security.SecurityElement.Escape(val?.ToString() ?? "")}</td>");
                        sb.AppendLine("</tr>");
                    }

                    sb.AppendLine("</table></body></html>");
                    File.WriteAllText(saveDialog.FileName, sb.ToString(), Encoding.UTF8);
                    StatusText.Text = $"✅ Exportação HTML concluída: {saveDialog.FileName}";
                }
                catch (Exception ex) { StatusText.Text = "❌ Erro ao exportar para HTML: " + ex.Message; }
            }
        }

        // =====================================================================
        // MÉTODO ExportSql_Click TOTALMENTE CORRIGIDO E COM LOGS
        // =====================================================================
        private void ExportSql_Click(object sender, RoutedEventArgs e)
        {
            var grid = GetSelectedGrid(out string reportType);
            if (grid == null || !(grid.ItemsSource is IEnumerable data) || !data.Cast<object>().Any())
            {
                StatusText.Text = "⚠️ Nenhum dado para exportar.";
                return;
            }

            try
            {
                var dialog = new SqlConnectionDialog();
                dialog.SetSuggestedDatabase("DNSAnalyzer");
                if (dialog.ShowDialog() == true)
                {
                    // CORREÇÃO: Converte os dados de PSObject para um formato que o ExportService entende.
                    var dataForSql = new List<dynamic>();
                    foreach (var item in data)
                    {
                        dataForSql.Add(PSObjectToDictionary(item));
                    }
                    
                    // Prepara o nome da tabela
                    string tableName = $"DNS_{reportType}_{DateTime.Now:yyyyMMdd_HHmmss}";

                    // ADICIONADO: Log de início
                    LogService.Write(_moduleName, $"Iniciando exportação para SQL. Tabela: '{tableName}', Banco: '{dialog.DatabaseName}'.");
                    
                    // Chama o serviço de exportação com os dados já convertidos
                    ExportService.ExportToSql(dataForSql, tableName, dialog.ConnectionString);
                    
                    string successMessage = $"✅ Exportação SQL concluída: {dialog.DatabaseName}";
                    StatusText.Text = successMessage;
                    
                    // ADICIONADO: Log de sucesso
                    LogService.Write(_moduleName, $"Exportação SQL para a tabela '{tableName}' concluída com sucesso.");
                }
            }
            catch (Exception ex)
            {
                string errorMessage = "❌ Erro ao exportar para SQL: " + ex.Message;
                StatusText.Text = errorMessage;
                
                // ADICIONADO: Log de erro detalhado
                LogService.Write(_moduleName, "ERRO na exportação para SQL: " + ex.ToString());
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
            var dict = new Dictionary<string, object>();
            if (psObject is PSObject pso)
            {
                foreach (var prop in pso.Properties)
                {
                    dict[prop.Name] = prop.Value;
                }
            }
            // CORRIGIDO: Adicionado para lidar com casos onde o objeto já é um dicionário
            else if (psObject is IDictionary<string, object> d)
            {
                return d;
            }
            return dict;
        }
    }
}