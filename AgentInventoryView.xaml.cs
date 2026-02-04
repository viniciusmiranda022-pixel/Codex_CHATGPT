using System.Windows.Controls;
using DirectoryAnalyzer.ViewModels;

namespace DirectoryAnalyzer.Views
{
    public partial class AgentInventoryView : UserControl
    {
        public AgentInventoryView()
        {
            InitializeComponent();
            DataContext = new AgentInventoryViewModel();
        }
    }
}
