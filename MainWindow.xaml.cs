using System.Windows;
using System.Windows.Controls;
using DirectoryAnalyzer.ViewModels;

namespace DirectoryAnalyzer
{
    public partial class MainWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
            if (NavigationMenu.Items.Count > 0)
            {
                NavigationMenu.SelectedIndex = 0;
            }
        }

        public void NavigateTo(string moduleName)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.NavigateTo(moduleName);
            }

            SelectMenuItem(moduleName);
        }

        private void SelectMenuItem(string moduleName)
        {
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                return;
            }

            foreach (var item in NavigationMenu.Items)
            {
                if (item is ListBoxItem listBoxItem && listBoxItem.Tag?.ToString() == moduleName)
                {
                    NavigationMenu.SelectedItem = listBoxItem;
                    return;
                }
            }
        }
    }
}
