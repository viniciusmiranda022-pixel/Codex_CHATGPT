using System.Windows;
using System.Windows.Controls;
using DirectoryAnalyzer.Views;
// A linha "using MahApps.Metro.Controls;" não é estritamente necessária se removermos a herança explícita.

namespace DirectoryAnalyzer
{
    // A única mudança é que removemos o ': Window' do final
    public partial class MainWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            if (NavigationMenu.Items.Count > 0)
            {
                NavigationMenu.SelectedIndex = 0;
            }
        }

        private void NavigationMenu_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ContentArea == null || !(NavigationMenu.SelectedItem is ListBoxItem selectedItem))
            {
                return;
            }
            
            string selectedContent = selectedItem.Content.ToString();
            
            switch (selectedContent)
            {
                case "DNS Analyzer": ContentArea.Content = new DnsAnalyzerView(); break;
                case "GPO Analyzer": ContentArea.Content = new GpoAnalyzerView(); break;
                case "SMB Shares Analyzer": ContentArea.Content = new SmbAnalyzerView(); break;
                case "Scheduled Tasks Analyzer": ContentArea.Content = new ScheduledTasksAnalyzerView(); break;
                case "Local Profiles Analyzer": ContentArea.Content = new LocalProfilesAnalyzerView(); break;
                case "Service Account Analyzer": ContentArea.Content = new InstalledServicesAnalyzerView(); break;
                case "Local Security Policy Analyzer": ContentArea.Content = new LocalSecurityPolicyAnalyzerView(); break;
                case "IIS AppPools Analyzer": ContentArea.Content = new IisAppPoolsAnalyzerView(); break;
                case "Trusts Analyzer": ContentArea.Content = new TrustsAnalyzerView(); break;
                case "ProxyAddresses Analyzer": ContentArea.Content = new ProxyAddressAnalyzerView(); break;
                default: ContentArea.Content = null; break;
            }
        }
    }
}