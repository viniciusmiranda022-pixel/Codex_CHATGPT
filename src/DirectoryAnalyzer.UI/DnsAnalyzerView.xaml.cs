using System.Windows.Controls;
using DirectoryAnalyzer.ViewModels;

namespace DirectoryAnalyzer.Views
{
    public partial class DnsAnalyzerView : UserControl
    {
        public DnsAnalyzerView()
        {
            InitializeComponent();
            DataContext = new DnsAnalyzerViewModel();
        }
    }
}
