using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using DirectoryAnalyzer.Services;

namespace DirectoryAnalyzer.ViewModels
{
    public sealed class AgentsViewModel : BaseAnalyzerViewModel
    {
        private readonly string _settingsPath;
        private readonly BrokerAgentRegistryService _registryService;
        private BrokerClientSettings _settings;
        private string _brokerBaseUrl;
        private string _clientCertThumbprint;
        private int _requestTimeoutSeconds;
        private int _pollIntervalSeconds;

        public AgentsViewModel()
        {
            _settingsPath = BrokerClientSettingsStore.ResolvePath();
            _settings = BrokerClientSettingsLoader.Load(_settingsPath);
            _registryService = new BrokerAgentRegistryService(_settings);

            SaveCommand = new RelayCommand(SaveSettings);
            DiscoverCommand = new AsyncRelayCommand(DiscoverAgentsAsync);

            LoadSettings();
            SetStatus("Configuração do broker pronta.", "Pronto");
        }

        public ObservableCollection<AgentDescriptorView> Agents { get; } = new ObservableCollection<AgentDescriptorView>();

        public ICommand SaveCommand { get; }
        public ICommand DiscoverCommand { get; }

        public string BrokerBaseUrl
        {
            get => _brokerBaseUrl;
            set => SetProperty(ref _brokerBaseUrl, value);
        }

        public string ClientCertThumbprint
        {
            get => _clientCertThumbprint;
            set => SetProperty(ref _clientCertThumbprint, value);
        }

        public int RequestTimeoutSeconds
        {
            get => _requestTimeoutSeconds;
            set => SetProperty(ref _requestTimeoutSeconds, value);
        }

        public int PollIntervalSeconds
        {
            get => _pollIntervalSeconds;
            set => SetProperty(ref _pollIntervalSeconds, value);
        }

        private void LoadSettings()
        {
            BrokerBaseUrl = _settings.BrokerBaseUrl;
            ClientCertThumbprint = _settings.ClientCertThumbprint;
            RequestTimeoutSeconds = _settings.RequestTimeoutSeconds;
            PollIntervalSeconds = _settings.PollIntervalSeconds;
        }

        private void SaveSettings()
        {
            _settings.BrokerBaseUrl = BrokerBaseUrl;
            _settings.ClientCertThumbprint = ClientCertThumbprint;
            _settings.RequestTimeoutSeconds = RequestTimeoutSeconds;
            _settings.PollIntervalSeconds = PollIntervalSeconds;

            BrokerClientSettingsStore.Save(_settingsPath, _settings);
            SetStatus("Configuração do broker salva com sucesso.", "Pronto");
        }

        private async Task DiscoverAgentsAsync()
        {
            var agents = await _registryService.GetAgentsAsync(CancellationToken.None);
            Agents.Clear();
            foreach (var agent in agents.Select(item => new AgentDescriptorView(item)))
            {
                Agents.Add(agent);
            }

            SetStatus($"Descoberta concluída. {Agents.Count} agentes conectados.", "Pronto");
        }
    }
}
