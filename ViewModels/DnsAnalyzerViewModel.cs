using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using DirectoryAnalyzer.Dialogs;
using DirectoryAnalyzer.Models;
using DirectoryAnalyzer.Modules;
using DirectoryAnalyzer.Modules.Dns;
using DirectoryAnalyzer.Services;
using Microsoft.Win32;

namespace DirectoryAnalyzer.ViewModels
{
    public class DnsAnalyzerViewModel : BaseAnalyzerViewModel
    {
        private const string ModuleName = "DnsAnalyzer";
        private readonly ICollector<DnsReport> _collector;
        private readonly ILogService _logService;
        private CancellationTokenSource _cancellationTokenSource;
        private int _selectedTabIndex;

        public DnsAnalyzerViewModel()
        {
            _logService = LogService.CreateLogger(ModuleName);
            _collector = new DnsCollector(new PowerShellService(), _logService);

            RunCommand = new AsyncRelayCommand(RunCollectionAsync, () => !IsBusy);
            CancelCommand = new RelayCommand(CancelCollection, () => IsBusy);
            ExportCsvCommand = new AsyncRelayCommand(() => ExportAsync(ExportFormat.Csv), () => !IsBusy);
            ExportXmlCommand = new AsyncRelayCommand(() => ExportAsync(ExportFormat.Xml), () => !IsBusy);
            ExportHtmlCommand = new AsyncRelayCommand(() => ExportAsync(ExportFormat.Html), () => !IsBusy);
            ExportSqlCommand = new AsyncRelayCommand(() => ExportAsync(ExportFormat.Sql), () => !IsBusy);

            SetStatus("✔️ Pronto para iniciar a coleta.", "Pronto");
        }

        public ObservableCollection<DnsZoneResult> Zones { get; } = new ObservableCollection<DnsZoneResult>();
        public ObservableCollection<DnsRecordResult> Records { get; } = new ObservableCollection<DnsRecordResult>();
        public ObservableCollection<DnsForwarderResult> Forwarders { get; } = new ObservableCollection<DnsForwarderResult>();

        public ICommand RunCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand ExportXmlCommand { get; }
        public ICommand ExportHtmlCommand { get; }
        public ICommand ExportSqlCommand { get; }

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetProperty(ref _selectedTabIndex, value);
        }

        private async Task RunCollectionAsync()
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            var progress = new Progress<string>(message => ProgressMessage = message);
            string correlationId = LogService.CreateCorrelationId();
            bool wasCanceled = false;
            bool success = false;
            int? itemCount = null;
            int? errorCount = null;

            IsBusy = true;
            UpdateCommandStates();
            ProgressMessage = "⏳ Coletando informações de DNS...";
            SetStatus("⏳ Coletando informações de DNS...", "Executando...");
            _logService.Info("Iniciando coleta de DNS.", correlationId);
            DashboardService.Instance.RecordModuleStart("DNS Analyzer");

            try
            {
                var report = await _collector.CollectAsync(_cancellationTokenSource.Token, progress);

                Zones.Clear();
                Records.Clear();
                Forwarders.Clear();

                foreach (var zone in report.Zones)
                {
                    Zones.Add(zone);
                }

                foreach (var record in report.Records)
                {
                    Records.Add(record);
                }

                foreach (var forwarder in report.Forwarders)
                {
                    Forwarders.Add(forwarder);
                }

                SetStatus($"✅ Coleta finalizada. {Zones.Count} zonas, {Records.Count} registros, {Forwarders.Count} encaminhadores.", "Concluído");
                _logService.Info("Coleta de DNS concluída.", correlationId);
                success = true;
                itemCount = Zones.Count + Records.Count + Forwarders.Count;
                errorCount = 0;
            }
            catch (OperationCanceledException)
            {
                SetStatus("⚠️ Coleta cancelada pelo usuário.", "Pronto");
                _logService.Warn("Coleta cancelada pelo usuário.", correlationId);
                wasCanceled = true;
            }
            catch (Exception ex)
            {
                SetStatus("❌ Erro durante a coleta: " + ex.Message, "Erro - ver log");
                _logService.Error("ERRO na coleta de DNS: " + ex, correlationId);
                errorCount = 1;
            }
            finally
            {
                ProgressMessage = string.Empty;
                IsBusy = false;
                UpdateCommandStates();
                DashboardService.Instance.RecordModuleFinish("DNS Analyzer", success, itemCount, errorCount, wasCanceled);
            }
        }

        private async Task ExportAsync(ExportFormat format)
        {
            var data = GetSelectedData();
            if (data == null || data.Count == 0)
            {
                SetStatus("⚠️ Nenhum dado para exportar.", "Pronto");
                return;
            }

            IsBusy = true;
            UpdateCommandStates();
            ProgressMessage = "⏳ Exportando dados...";
            SetStatus("⏳ Exportando dados...", "Executando...");
            string correlationId = LogService.CreateCorrelationId();
            _logService.Info($"Iniciando exportação {format}.", correlationId);

            try
            {
                switch (format)
                {
                    case ExportFormat.Sql:
                        await ExportSqlAsync(data);
                        break;
                    default:
                        var defaultName = $"DNS_{GetCurrentTabName()}_{DateTime.Now:yyyyMMdd_HHmmss}";
                        var dialog = new SaveFileDialog
                        {
                            FileName = defaultName,
                            Filter = GetFilter(format)
                        };

                        if (dialog.ShowDialog() != true)
                        {
                            return;
                        }

                        await Task.Run(() => ExecuteExport(format, data, dialog.FileName));
                        SetStatus($"✅ Exportação {format} concluída: {dialog.FileName}", "Concluído");
                        _logService.Info($"Exportação {format} concluída: {dialog.FileName}", correlationId);
                        break;
                }
            }
            catch (Exception ex)
            {
                SetStatus($"❌ Erro ao exportar {format}: {ex.Message}", "Erro - ver log");
                _logService.Error($"ERRO na exportação {format}: {ex}", correlationId);
            }
            finally
            {
                ProgressMessage = string.Empty;
                IsBusy = false;
                UpdateCommandStates();
            }
        }

        private void ExecuteExport(ExportFormat format, ObservableCollection<object> data, string fileName)
        {
            switch (format)
            {
                case ExportFormat.Csv:
                    ExportService.ExportToCsv(data, fileName);
                    break;
                case ExportFormat.Xml:
                    ExportService.ExportToXml(data, fileName, $"DNS_{GetCurrentTabName()}");
                    break;
                case ExportFormat.Html:
                    ExportService.ExportToHtml(data, fileName, $"DNS - {GetCurrentTabName()}");
                    break;
            }
        }

        private async Task ExportSqlAsync(ObservableCollection<object> data)
        {
            var dialog = new SqlConnectionDialog();
            dialog.SetSuggestedDatabase("DNSAnalyzer");
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            string tableName = $"DNS_{GetCurrentTabName()}_{DateTime.Now:yyyyMMdd_HHmmss}";
            string correlationId = LogService.CreateCorrelationId();
            _logService.Info($"Iniciando exportação SQL. Tabela: '{tableName}', Banco: '{dialog.DatabaseName}'.", correlationId);
            await Task.Run(() => ExportService.ExportToSql(data, tableName, dialog.ConnectionString));
            SetStatus($"✅ Exportação SQL concluída: {dialog.DatabaseName}", "Concluído");
            _logService.Info($"Exportação SQL concluída: {dialog.DatabaseName}", correlationId);
        }

        private void CancelCollection()
        {
            if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
            }
        }

        private void UpdateCommandStates()
        {
            (RunCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExportCsvCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (ExportXmlCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (ExportHtmlCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (ExportSqlCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }

        private ObservableCollection<object> GetSelectedData()
        {
            switch (SelectedTabIndex)
            {
                case 0:
                    return new ObservableCollection<object>(Zones.Cast<object>());
                case 1:
                    return new ObservableCollection<object>(Records.Cast<object>());
                case 2:
                    return new ObservableCollection<object>(Forwarders.Cast<object>());
                default:
                    return new ObservableCollection<object>();
            }
        }

        private string GetCurrentTabName()
        {
            switch (SelectedTabIndex)
            {
                case 0:
                    return "Zonas";
                case 1:
                    return "Registros";
                case 2:
                    return "Encaminhadores";
                default:
                    return "Relatorio";
            }
        }

        private static string GetFilter(ExportFormat format)
        {
            switch (format)
            {
                case ExportFormat.Csv:
                    return "CSV Files (*.csv)|*.csv";
                case ExportFormat.Xml:
                    return "XML Files (*.xml)|*.xml";
                case ExportFormat.Html:
                    return "HTML Files (*.html)|*.html";
                default:
                    return "All Files (*.*)|*.*";
            }
        }

        private enum ExportFormat
        {
            Csv,
            Xml,
            Html,
            Sql
        }
    }
}
