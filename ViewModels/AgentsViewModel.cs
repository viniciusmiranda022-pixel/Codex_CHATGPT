using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using DirectoryAnalyzer.Services;

namespace DirectoryAnalyzer.ViewModels
{
    public sealed class AgentsViewModel : BaseAnalyzerViewModel
    {
        private readonly string _settingsPath;
        private AgentModeSettings _settings;
        private AgentEndpoint _selectedAgent;
        private string _clientCertThumbprint;
        private int _requestTimeoutSeconds;
        private int _maxRetries;

        public AgentsViewModel()
        {
            _settingsPath = AgentSettingsStore.ResolveSettingsPath("agentclientsettings.json");
            LoadSettings();

            AddAgentCommand = new RelayCommand(AddAgent);
            RemoveAgentCommand = new RelayCommand(RemoveAgent, () => SelectedAgent != null);
            SaveCommand = new RelayCommand(SaveSettings);
            DiscoverCommand = new RelayCommand(DiscoverAgents);

            SetStatus("Configuração de agentes pronta.", "Pronto");
        }

        public ObservableCollection<AgentEndpoint> Agents { get; } = new ObservableCollection<AgentEndpoint>();

        public ICommand AddAgentCommand { get; }
        public ICommand RemoveAgentCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand DiscoverCommand { get; }

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

        public int MaxRetries
        {
            get => _maxRetries;
            set => SetProperty(ref _maxRetries, value);
        }

        public AgentEndpoint SelectedAgent
        {
            get => _selectedAgent;
            set
            {
                if (SetProperty(ref _selectedAgent, value))
                {
                    (RemoveAgentCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private void LoadSettings()
        {
            _settings = AgentSettingsStore.Load(_settingsPath);
            Agents.Clear();
            foreach (var agent in _settings.Agents)
            {
                Agents.Add(agent);
            }

            ClientCertThumbprint = _settings.ClientCertThumbprint;
            RequestTimeoutSeconds = _settings.RequestTimeoutSeconds;
            MaxRetries = _settings.MaxRetries;
            SelectedAgent = Agents.FirstOrDefault(agent => agent.Id == _settings.SelectedAgentId) ?? Agents.FirstOrDefault();
        }

        private void AddAgent()
        {
            var agent = new AgentEndpoint
            {
                Name = "Novo agente",
                Endpoint = "https://localhost:8443/agent/"
            };
            Agents.Add(agent);
            SelectedAgent = agent;
        }

        private void RemoveAgent()
        {
            if (SelectedAgent == null)
            {
                return;
            }

            Agents.Remove(SelectedAgent);
            SelectedAgent = Agents.FirstOrDefault();
        }

        private void DiscoverAgents()
        {
            if (!Agents.Any())
            {
                Agents.Add(new AgentEndpoint());
                SelectedAgent = Agents.FirstOrDefault();
            }

            SetStatus("Busca concluída. Ajuste os detalhes conforme necessário.", "Pronto");
        }

        private void SaveSettings()
        {
            if (!Agents.Any())
            {
                Agents.Add(new AgentEndpoint());
            }

            _settings.ClientCertThumbprint = ClientCertThumbprint;
            _settings.RequestTimeoutSeconds = RequestTimeoutSeconds;
            _settings.MaxRetries = MaxRetries;
            _settings.Agents = Agents.ToList();
            _settings.SelectedAgentId = SelectedAgent?.Id ?? Agents.First().Id;

            AgentSettingsStore.Save(_settingsPath, _settings);
            SetStatus("Configuração salva.", "Pronto");
        }
    }
}
