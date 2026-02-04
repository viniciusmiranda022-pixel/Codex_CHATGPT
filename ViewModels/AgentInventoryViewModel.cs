using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using DirectoryAnalyzer.AgentContracts;
using DirectoryAnalyzer.Services;

namespace DirectoryAnalyzer.ViewModels
{
    public sealed class AgentInventoryViewModel : BaseAnalyzerViewModel
    {
        private const string ModuleName = "AgentInventory";
        private readonly ILogService _logService;
        private readonly AgentClientService _agentClient;
        private CancellationTokenSource _cancellationTokenSource;

        public AgentInventoryViewModel()
        {
            _logService = LogService.CreateLogger(ModuleName);
            var settingsPath = ResolveSettingsPath("agentclientsettings.json");
            _agentClient = new AgentClientService(settingsPath, _logService);

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
                Users.Clear();
                var result = await _agentClient.GetUsersAsync(includeDisabled: false, _cancellationTokenSource.Token);
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

        private static string ResolveSettingsPath(string fileName)
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var sharedPath = System.IO.Path.Combine(programData, "DirectoryAnalyzer", fileName);
            if (System.IO.File.Exists(sharedPath))
            {
                return sharedPath;
            }

            return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
        }
    }
}
