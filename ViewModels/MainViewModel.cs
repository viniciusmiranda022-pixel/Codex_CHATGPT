using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DirectoryAnalyzer.Views;

namespace DirectoryAnalyzer.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly Dictionary<string, Func<object>> _viewFactory;
        private object _currentView;

        public MainViewModel()
        {
            _viewFactory = new Dictionary<string, Func<object>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Dashboard"] = () => new DashboardView(),
                ["DNS Analyzer"] = () => new DnsAnalyzerView(),
                ["GPO Analyzer"] = () => new GpoAnalyzerView(),
                ["SMB Shares Analyzer"] = () => new SmbAnalyzerView(),
                ["Scheduled Tasks Analyzer"] = () => new ScheduledTasksAnalyzerView(),
                ["Local Profiles Analyzer"] = () => new LocalProfilesAnalyzerView(),
                ["Service Account Analyzer"] = () => new InstalledServicesAnalyzerView(),
                ["Local Security Policy Analyzer"] = () => new LocalSecurityPolicyAnalyzerView(),
                ["IIS AppPools Analyzer"] = () => new IisAppPoolsAnalyzerView(),
                ["Trusts Analyzer"] = () => new TrustsAnalyzerView(),
                ["ProxyAddresses Analyzer"] = () => new ProxyAddressAnalyzerView(),
                ["Agent Inventory"] = () => new AgentInventoryView(),
                ["Agents"] = () => new AgentsView()
            };

            NavigateCommand = new ParameterRelayCommand(parameter =>
            {
                if (parameter is string target)
                {
                    NavigateTo(target);
                }
            });

            NavigateTo("Dashboard");
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public ICommand NavigateCommand { get; }

        public object CurrentView
        {
            get => _currentView;
            private set
            {
                if (Equals(_currentView, value))
                {
                    return;
                }

                _currentView = value;
                OnPropertyChanged();
            }
        }

        public void NavigateTo(string moduleName)
        {
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                return;
            }

            if (_viewFactory.TryGetValue(moduleName, out var createView))
            {
                CurrentView = createView();
            }
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

    }
}
