using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using DirectoryAnalyzer.Agent.Client;
using DirectoryAnalyzer.Agent.Contracts;
using DirectoryAnalyzer.Services;

namespace DirectoryAnalyzer.ViewModels
{
    public sealed class AgentInventoryViewModel : BaseAnalyzerViewModel
    {
        private const string ModuleName = "AgentInventory";
        private readonly ILogService _logService;
        private readonly string _settingsPath;
        private AgentClient _agentClient;
        private CancellationTokenSource _cancellationTokenSource;

        public AgentInventoryViewModel()
        {
            _logService = LogService.CreateLogger(ModuleName);
            _settingsPath = AgentSettingsStore.ResolveSettingsPath("agentclientsettings.json");

            RunCommand = new AsyncRelayCommand(RunAsync, () => !IsBusy);
            CancelCommand = new RelayCommand(Cancel, () => IsBusy);

            SetStatus("✔️ Pronto para consultar o agente.", "Pronto");
        }

        public ObservableCollection<UserRecord> Users { get; } = new ObservableCollection<UserRecord>();

        public ICommand RunCommand { get; }
        public ICommand CancelCommand { get; }

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
                var settings = AgentSettingsStore.Load(_settingsPath);
                if (!settings.AgentModeEnabled)
                {
                    SetStatus("⚠️ Agent Mode desabilitado. Ative para usar o agente.", "Pronto");
                    return;
                }

                var agent = settings.Agents?.FirstOrDefault(entry => entry.Id == settings.SelectedAgentId)
                            ?? settings.Agents?.FirstOrDefault();
                if (agent == null)
                {
                    throw new InvalidOperationException("Nenhum agente configurado.");
                }

                _agentClient = new AgentClient(new AgentClientOptions
                {
                    Endpoint = new Uri(agent.Endpoint),
                    ClientCertThumbprint = settings.ClientCertThumbprint,
                    AllowedServerThumbprints = agent.AllowedThumbprints ?? Array.Empty<string>(),
                    Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds),
                    MaxRetries = settings.MaxRetries
                });

                var correlationId = Guid.NewGuid().ToString("N");
                Users.Clear();
                var result = await _agentClient.GetUsersAsync(includeDisabled: false, correlationId, _cancellationTokenSource.Token);
                foreach (var user in result.Users)
                {
                    Users.Add(user);
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
