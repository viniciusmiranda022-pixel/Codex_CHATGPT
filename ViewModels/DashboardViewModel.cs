using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using DirectoryAnalyzer.Services;

namespace DirectoryAnalyzer.ViewModels
{
    public class DashboardViewModel : INotifyPropertyChanged
    {
        private readonly DashboardService _dashboardService;
        private int _kpiColumns = 3;

        public DashboardViewModel(Action<string> navigateAction)
        {
            _dashboardService = DashboardService.Instance;

            NavigateCommand = new ParameterRelayCommand(parameter =>
            {
                if (parameter is string target && !string.IsNullOrWhiteSpace(target))
                {
                    navigateAction?.Invoke(target);
                }
            });

            QuickActions = new ObservableCollection<QuickActionCard>
            {
                new QuickActionCard("DNS Analyzer", "Consultar zonas e registros DNS", "DNS Analyzer"),
                new QuickActionCard("SMB Shares Analyzer", "Listar permissões de compartilhamentos", "SMB Shares Analyzer"),
                new QuickActionCard("Service Account Analyzer", "Serviços instalados no domínio", "Service Account Analyzer"),
                new QuickActionCard("GPO Analyzer", "Inventário de políticas e vínculos", "GPO Analyzer")
            };

            KpiCards = new ObservableCollection<KpiCard>
            {
                new KpiCard("Última execução", "N/D", "Sem histórico", "\uE823"),
                new KpiCard("Módulo executado", "N/D", "Último módulo", "\uE8F1"),
                new KpiCard("Itens coletados", "N/D", "Total da execução", "\uE8A5"),
                new KpiCard("Erros", "N/D", "Última execução", "\uE783"),
                new KpiCard("Tempo da última execução", "N/D", "Duração registrada", "\uE917"),
                new KpiCard("Status", "N/D", "Resultado da execução", "\uE73E")
            };

            RecentActivities = new ObservableCollection<ActivityRow>();
            RefreshRecentEntries();
            RefreshKpis();

            LogFilePath = _dashboardService.LogFilePath;
            AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "N/D";
            StatusServiceState = "OK";

            _dashboardService.ActivityAdded += OnActivityAdded;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<KpiCard> KpiCards { get; }

        public ObservableCollection<QuickActionCard> QuickActions { get; }

        public ObservableCollection<ActivityRow> RecentActivities { get; }

        public ICommand NavigateCommand { get; }

        public string LogFilePath { get; }

        public string AppVersion { get; }

        public string StatusServiceState { get; }

        public int KpiColumns
        {
            get => _kpiColumns;
            set
            {
                if (_kpiColumns == value)
                {
                    return;
                }

                _kpiColumns = value;
                OnPropertyChanged();
            }
        }

        public void UpdateLayoutWidth(double width)
        {
            if (width >= 1300)
            {
                KpiColumns = 4;
            }
            else if (width >= 980)
            {
                KpiColumns = 3;
            }
            else
            {
                KpiColumns = 2;
            }
        }

        private void OnActivityAdded(object sender, DashboardActivityEntry e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                RefreshRecentEntries();
                RefreshKpis();
            });
        }

        private void RefreshRecentEntries()
        {
            RecentActivities.Clear();
            foreach (var entry in _dashboardService.RecentEntries)
            {
                RecentActivities.Add(new ActivityRow(
                    entry.Timestamp.ToString("dd/MM/yyyy HH:mm"),
                    entry.Module,
                    entry.Status,
                    FormatDuration(entry.DurationSeconds)));
            }
        }

        private void RefreshKpis()
        {
            var lastEntry = _dashboardService.LastCompletedEntry;
            if (lastEntry == null)
            {
                UpdateKpi("Última execução", "N/D", "Sem histórico");
                UpdateKpi("Módulo executado", "N/D", "Último módulo");
                UpdateKpi("Itens coletados", "N/D", "Total da execução");
                UpdateKpi("Erros", "N/D", "Última execução");
                UpdateKpi("Tempo da última execução", "N/D", "Duração registrada");
                UpdateKpi("Status", "N/D", "Resultado da execução");
                return;
            }

            string status = lastEntry.Status == "Concluído" && (lastEntry.ErrorCount ?? 0) == 0
                ? "OK"
                : "Com erros";

            UpdateKpi("Última execução", lastEntry.Timestamp.ToString("dd/MM/yyyy HH:mm"), "Data/hora da última execução");
            UpdateKpi("Módulo executado", lastEntry.Module ?? "N/D", "Último módulo");
            UpdateKpi("Itens coletados", lastEntry.ItemCount?.ToString() ?? "N/D", "Total da execução");
            UpdateKpi("Erros", lastEntry.ErrorCount?.ToString() ?? "N/D", "Última execução");
            UpdateKpi("Tempo da última execução", FormatDuration(lastEntry.DurationSeconds), "Duração registrada");
            UpdateKpi("Status", status, "Resultado da execução");
        }

        private void UpdateKpi(string title, string value, string subtext)
        {
            var card = KpiCards.FirstOrDefault(item => item.Title == title);
            if (card == null)
            {
                return;
            }

            card.Value = value;
            card.Subtext = subtext;
        }

        private static string FormatDuration(double? seconds)
        {
            if (seconds == null)
            {
                return "N/D";
            }

            var duration = TimeSpan.FromSeconds(seconds.Value);
            if (duration.TotalHours >= 1)
            {
                return $"{duration.Hours}h {duration.Minutes}m";
            }

            if (duration.TotalMinutes >= 1)
            {
                return $"{duration.Minutes}m {duration.Seconds}s";
            }

            return $"{duration.Seconds}s";
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class KpiCard : INotifyPropertyChanged
    {
        private string _value;
        private string _subtext;

        public KpiCard(string title, string value, string subtext, string icon)
        {
            Title = title;
            _value = value;
            _subtext = subtext;
            Icon = icon;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string Title { get; }

        public string Icon { get; }

        public string Value
        {
            get => _value;
            set
            {
                if (_value == value)
                {
                    return;
                }

                _value = value;
                OnPropertyChanged();
            }
        }

        public string Subtext
        {
            get => _subtext;
            set
            {
                if (_subtext == value)
                {
                    return;
                }

                _subtext = value;
                OnPropertyChanged();
            }
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class QuickActionCard
    {
        public QuickActionCard(string title, string description, string target)
        {
            Title = title;
            Description = description;
            Target = target;
        }

        public string Title { get; }

        public string Description { get; }

        public string Target { get; }
    }

    public class ActivityRow
    {
        public ActivityRow(string timestamp, string module, string status, string duration)
        {
            Timestamp = timestamp;
            Module = module;
            Status = status;
            Duration = duration;
        }

        public string Timestamp { get; }

        public string Module { get; }

        public string Status { get; }

        public string Duration { get; }
    }
}
