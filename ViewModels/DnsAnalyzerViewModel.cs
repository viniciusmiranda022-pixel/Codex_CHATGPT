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

            StatusMessage = "✔️ Pronto para iniciar a coleta.";
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

            IsBusy = true;
            UpdateCommandStates();
            ProgressMessage = "⏳ Coletando informações de DNS...";
            StatusMessage = "⏳ Coletando informações de DNS...";

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

                StatusMessage = $"✅ Coleta finalizada. {Zones.Count} zonas, {Records.Count} registros, {Forwarders.Count} encaminhadores.";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "⚠️ Coleta cancelada pelo usuário.";
                _logService.Warn("Coleta cancelada pelo usuário.");
            }
            catch (Exception ex)
            {
                StatusMessage = "❌ Erro durante a coleta: " + ex.Message;
                _logService.Error("ERRO na coleta de DNS: " + ex);
            }
            finally
            {
                ProgressMessage = string.Empty;
                IsBusy = false;
                UpdateCommandStates();
            }
        }

        private async Task ExportAsync(ExportFormat format)
        {
            var data = GetSelectedData();
            if (data == null || data.Count == 0)
            {
                StatusMessage = "⚠️ Nenhum dado para exportar.";
                return;
            }

            IsBusy = true;
            UpdateCommandStates();
            ProgressMessage = "⏳ Exportando dados...";

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
                        StatusMessage = $"✅ Exportação {format} concluída: {dialog.FileName}";
                        _logService.Info($"Exportação {format} concluída: {dialog.FileName}");
                        break;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Erro ao exportar {format}: {ex.Message}";
                _logService.Error($"ERRO na exportação {format}: {ex}");
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
            _logService.Info($"Iniciando exportação SQL. Tabela: '{tableName}', Banco: '{dialog.DatabaseName}'.");
            await Task.Run(() => ExportService.ExportToSql(data, tableName, dialog.ConnectionString));
            StatusMessage = $"✅ Exportação SQL concluída: {dialog.DatabaseName}";
            _logService.Info($"Exportação SQL concluída: {dialog.DatabaseName}");
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
