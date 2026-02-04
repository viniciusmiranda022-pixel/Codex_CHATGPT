using System.Windows;
using System.Windows.Controls;
using DirectoryAnalyzer.ViewModels;

namespace DirectoryAnalyzer.Views
{
    public partial class DashboardView : UserControl
    {
        public DashboardView()
        {
            InitializeComponent();
            DataContext = new DashboardViewModel(NavigateToModule);
            SizeChanged += DashboardView_SizeChanged;
        }

        private void DashboardView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DataContext is DashboardViewModel viewModel)
            {
                viewModel.UpdateLayoutWidth(e.NewSize.Width);
            }
        }

        private void NavigateToModule(string moduleName)
        {
            var mainWindow = Window.GetWindow(this) as MainWindow;
            mainWindow?.NavigateTo(moduleName);
        }
    }
}
