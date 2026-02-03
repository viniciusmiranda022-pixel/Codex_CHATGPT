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
    public partial class IisAppPoolsAnalyzerView : UserControl
    {
        private readonly PowerShellService _powerShellService;
        private const string ModuleName = "IisAppPoolsAnalyzer";
        private readonly ILogService _logService;

        public IisAppPoolsAnalyzerView()
        {
            InitializeComponent();
            _powerShellService = new PowerShellService();
            _logService = LogService.CreateLogger(ModuleName);
            UpdateStatus("✔️ Pronto para iniciar a coleta.", "Pronto");
        }

        private async void RunIisCollection(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button != null) button.IsEnabled = false;
            string correlationId = LogService.CreateCorrelationId();

            ProgressText.Visibility = Visibility.Visible;
            UpdateStatus("⏳ Coletando informações de AppPools do IIS. Isso pode demorar...", "Executando...");

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
                    Import-Module ActiveDirectory -ErrorAction SilentlyContinue; if (-not (Get-Module ActiveDirectory)) { throw 'Módulo ActiveDirectory não encontrado.' }
                    try { $serverList = Get-ADComputer -Filter ""$AttributeName -eq '$AttributeValue'"" | Select-Object -ExpandProperty Name } catch { throw ""Falha ao buscar computadores no AD: $($_.Exception.Message)"" }
                    if (-not $serverList) { return }

                    $allResults = foreach ($serverName in $serverList) {
                        if (-not (Test-Connection -ComputerName $serverName -Count 1 -Quiet -ErrorAction SilentlyContinue)) {
                            [PSCustomObject]@{ ComputerName = $serverName; ApplicationPool = 'ERRO DE CONEXÃO'; Status = 'N/A'; CustomIdentity = 'Servidor inacessível'; SitesVinculados = 'N/A' }; continue
                        }
                        
                        $scriptBlock = {
                            if (-not (Get-Module WebAdministration -ListAvailable)) {
                                return [PSCustomObject]@{ ComputerName = $env:COMPUTERNAME; ApplicationPool = 'ERRO DE MÓDULO'; Status = 'N/A'; CustomIdentity = 'Módulo WebAdministration não encontrado'; SitesVinculados = 'N/A' }
                            }
                            Import-Module WebAdministration
                            
                            try {
                                $pools = Get-ChildItem IIS:\AppPools -ErrorAction Stop
                                $sites = Get-Website -ErrorAction Stop
                            } catch {
                                return [PSCustomObject]@{ ComputerName = $env:COMPUTERNAME; ApplicationPool = 'ERRO DE COLETA IIS'; Status = 'N/A'; CustomIdentity = 'Falha ao executar Get-ChildItem ou Get-Website. O IIS está instalado e rodando?'; SitesVinculados = 'N/A' }
                            }
                            
                            foreach ($pool in $pools) {
                                $vinculados = $sites | Where-Object { $_.applicationPool -eq $pool.Name } | Select-Object -ExpandProperty Name
                                [PSCustomObject]@{
                                    ComputerName = $env:COMPUTERNAME
                                    ApplicationPool = $pool.Name
                                    Status = $pool.State
                                    CustomIdentity = if ($pool.processModel.identityType -eq 'SpecificUser') { $pool.processModel.userName } else { $pool.processModel.identityType }
                                    SitesVinculados = if ($vinculados) { $vinculados -join ', ' } else { 'Nenhum' }
                                }
                            }
                        }

                        try {
                            Invoke-Command -ComputerName $serverName -ScriptBlock $scriptBlock -ErrorAction Stop
                        } catch {
                            [PSCustomObject]@{ ComputerName = $serverName; ApplicationPool = 'ERRO DE EXECUÇÃO REMOTA'; Status = 'N/A'; CustomIdentity = $_.Exception.Message; SitesVinculados = 'N/A' }
                        }
                    }
                    $allResults
                ";
                var scriptParameters = new Dictionary<string, object> { { "AttributeName", scopeAttribute }, { "AttributeValue", scopeValue } };
                
                var results = await _powerShellService.ExecuteScriptAsync(scriptText, scriptParameters);
                
                IisGrid.ItemsSource = results;
                AnimateGrid(IisGrid);

                var errorRows = results.Count(item => (item as IDictionary<string, object>)["ApplicationPool"].ToString().Contains("ERRO"));
                var successRows = results.Count - errorRows;
                UpdateStatus($"✅ Coleta concluída. {successRows} AppPools encontrados com {errorRows} erros de acesso/conexão.", "Concluído");
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
            if (!(IisGrid.ItemsSource is IEnumerable<dynamic> data) || !data.Any()) { UpdateStatus("⚠️ Não há dados para exportar.", "Pronto"); return; }
            var saveDialog = new SaveFileDialog { FileName = $"IIS_AppPools_{DateTime.Now:yyyyMMdd_HHmmss}.csv", Filter = "CSV Files (*.csv)|*.csv" };
            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    _logService.Info($"Iniciando exportação CSV: {saveDialog.FileName}", correlationId);
                    var sb = new StringBuilder();
                    if (data.FirstOrDefault() is IDictionary<string, object> firstItem) sb.AppendLine(string.Join(";", firstItem.Keys));
                    foreach (var item in data)
                    {
                        if(item is IDictionary<string, object> itemDict)
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
            if (!(IisGrid.ItemsSource is IEnumerable<dynamic> data) || !data.Any()) { UpdateStatus("⚠️ Não há dados para exportar.", "Pronto"); return; }
            var saveDialog = new SaveFileDialog { FileName = $"IIS_AppPools_{DateTime.Now:yyyyMMdd_HHmmss}.xml", Filter = "XML Files (*.xml)|*.xml" };
            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    _logService.Info($"Iniciando exportação XML: {saveDialog.FileName}", correlationId);
                    using (var writer = new XmlTextWriter(saveDialog.FileName, Encoding.UTF8))
                    {
                        writer.Formatting = Formatting.Indented;
                        writer.WriteStartDocument(); writer.WriteStartElement("IIS_AppPools");
                        foreach (var item in data)
                        {
                            if (item is IDictionary<string, object> itemDict)
                            {
                                writer.WriteStartElement("AppPool");
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
            if (!(IisGrid.ItemsSource is IEnumerable<dynamic> data) || !data.Any()) { UpdateStatus("⚠️ Não há dados para exportar.", "Pronto"); return; }
            var saveDialog = new SaveFileDialog { FileName = $"IIS_AppPools_{DateTime.Now:yyyyMMdd_HHmmss}.html", Filter = "HTML Files (*.html)|*.html" };
            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    _logService.Info($"Iniciando exportação HTML: {saveDialog.FileName}", correlationId);
                    var sb = new StringBuilder();
                    sb.AppendLine("<html><head><title>Relatório de IIS AppPools</title><style>body{font-family:sans-serif}table{border-collapse:collapse;width:100%}td,th{border:1px solid #ddd;padding:8px}tr:nth-child(even){background-color:#f2f2f2}</style></head><body>");
                    sb.AppendLine("<h2>Relatório de IIS AppPools</h2><table>");
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
            if (!(IisGrid.ItemsSource is IEnumerable<dynamic> data) || !data.Any()) { UpdateStatus("⚠️ Não há dados para exportar.", "Pronto"); return; }
            try
            {
                var dialog = new SqlConnectionDialog();
                string domainName = _powerShellService.GetDomainNetBiosName();
                dialog.SetSuggestedDatabase(domainName);
                if (dialog.ShowDialog() == true)
                {
                    var sqlManager = new SqlManagerService(dialog.ServerName, dialog.DatabaseName, dialog.ConnectionString);
                    sqlManager.EnsureDatabaseExists();
                    
                    string tableName = $"IIS_AppPools_{DateTime.Now:yyyyMMdd_HHmmss}";
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
