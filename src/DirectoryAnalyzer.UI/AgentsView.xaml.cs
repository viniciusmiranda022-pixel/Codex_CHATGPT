using System.Windows.Controls;
using DirectoryAnalyzer.ViewModels;

namespace DirectoryAnalyzer.Views
{
    public partial class AgentsView : UserControl
    {
        public AgentsView()
        {
            InitializeComponent();
            DataContext = new AgentsViewModel();
        }
    }
}
