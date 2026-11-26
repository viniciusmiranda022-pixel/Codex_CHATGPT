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
        private const string DashboardName = "Inventário via agente";
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
            DashboardService.Instance.RecordModuleStart(DashboardName);

            CollectionOutcome outcome;
            try
            {
                var request = new JobRequest
                {
                    CorrelationId = Guid.NewGuid().ToString("N"),
                    ModuleName = "GetUsers",
                    RequestedBy = Environment.UserName,
                    Parameters = { ["IncludeDisabled"] = "false" }
                };

                outcome = await _brokerJobService.ExecuteAsync(request, _cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                _logService.Error("Erro ao consultar agente: " + ex);
                outcome = CollectionOutcome.Failed("UnexpectedError", ex.Message);
            }

            try
            {
                Apply(outcome);
            }
            finally
            {
                ProgressMessage = string.Empty;
                IsBusy = false;
                UpdateCommandStates();
            }
        }

        /// <summary>
        /// Aplica o desfecho à tela e ao painel. Só limpa e repopula a grade quando o agente executou a
        /// coleta; recusa ou indisponibilidade não apagam o último resultado nem viram "0 usuários".
        /// </summary>
        private void Apply(CollectionOutcome outcome)
        {
            if (outcome.IsSuccess)
            {
                Users.Clear();
                foreach (var item in outcome.Result.Items)
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

            var message = outcome.DescribeForUser("usuários");
            SetStatus(message, outcome.DashboardStatus);
            if (outcome.IsSuccess || outcome.Kind == CollectionOutcomeKind.Canceled)
            {
                _logService.Info(message);
            }
            else
            {
                _logService.Warn(message);
            }

            DashboardService.Instance.RecordModuleOutcome(DashboardName, outcome);
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
