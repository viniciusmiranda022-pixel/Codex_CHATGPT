using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using DirectoryAnalyzer.Agent.Contracts;
using DirectoryAnalyzer.Contracts;
using DirectoryAnalyzer.Services;

namespace DirectoryAnalyzer.ViewModels
{
    public sealed class AgentInventoryViewModel : BaseAnalyzerViewModel
    {
        private const string ModuleName = "AgentInventory";
        private readonly ILogService _logService;
        private readonly BrokerJobService _brokerJobService;
        private CancellationTokenSource _cancellationTokenSource;

        public AgentInventoryViewModel()
        {
            _logService = LogService.CreateLogger(ModuleName);
            var settings = BrokerClientSettingsLoader.Load(BrokerClientSettingsStore.ResolvePath());
            _brokerJobService = new BrokerJobService(settings);

            RunCommand = new AsyncRelayCommand(RunAsync, () => !IsBusy);
            CancelCommand = new RelayCommand(Cancel, () => IsBusy);

            SetStatus("✔️ Pronto para consultar o agente.", "Pronto");
        }

        public ObservableCollection<UserRecord> Users { get; } = new ObservableCollection<UserRecord>();

        private async Task RunAsync()
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            IsBusy = true;
            UpdateCommandStates();
            ProgressMessage = "⏳ Consultando usuários via agente...";
            SetStatus("⏳ Consultando usuários via agente...", "Executando...");

            try
            {
                var request = new JobRequest
                {
                    CorrelationId = Guid.NewGuid().ToString("N"),
                    ModuleName = "GetUsers",
                    RequestedBy = Environment.UserName,
                    Parameters = { ["IncludeDisabled"] = "false" }
                };

                var moduleResult = await _brokerJobService.RunJobAsync(request, _cancellationTokenSource.Token);

                Users.Clear();
                if (moduleResult?.Items != null)
                {
                    foreach (var item in moduleResult.Items)
                    {
                        item.Columns.TryGetValue("SamAccountName", out var sam);
                        item.Columns.TryGetValue("DisplayName", out var display);
                        item.Columns.TryGetValue("Enabled", out var enabledText);

                        Users.Add(new UserRecord
                        {
                            SamAccountName = sam,
                            DisplayName = display,
                            Enabled = bool.TryParse(enabledText, out var enabled) && enabled
                        });
                    }
                }

                SetStatus($"✅ Consulta finalizada. {Users.Count} usuários.", "Concluído");
            }
            catch (OperationCanceledException)
            {
                SetStatus("⚠️ Consulta cancelada pelo usuário.", "Pronto");
            }
            catch (Exception ex)
            {
                _logService.Error("Erro ao consultar agente: " + ex);
                SetStatus("❌ Erro ao consultar o agente. Ver log.", "Erro");
            }
            finally
            {
                ProgressMessage = string.Empty;
                IsBusy = false;
                UpdateCommandStates();
            }
        }

        private void Cancel()
        {
            _cancellationTokenSource?.Cancel();
        }

        private void UpdateCommandStates()
        {
            (RunCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

    }
}
