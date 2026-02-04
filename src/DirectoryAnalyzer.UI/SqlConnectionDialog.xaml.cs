using System.Windows;

namespace DirectoryAnalyzer.Dialogs
{
    public partial class SqlConnectionDialog : Window
    {
        public string ConnectionString { get; private set; }
        public string ServerName { get; private set; }
        public string DatabaseName { get; private set; }

        public SqlConnectionDialog()
        {
            InitializeComponent();
        }

        // NOVO MÉTODO para preencher o campo do banco de dados
        public void SetSuggestedDatabase(string dbName)
        {
            if(DatabaseBox != null)
            {
                DatabaseBox.Text = dbName;
            }
        }

        private void UseWindowsAuth_Checked(object sender, RoutedEventArgs e)
        {
            if (UsernameBox != null) UsernameBox.IsEnabled = false;
            if (PasswordBox != null) PasswordBox.IsEnabled = false;
        }

        private void UseWindowsAuth_Unchecked(object sender, RoutedEventArgs e)
        {
            if (UsernameBox != null) UsernameBox.IsEnabled = true;
            if (PasswordBox != null) PasswordBox.IsEnabled = true;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ServerBox.Text) || string.IsNullOrWhiteSpace(DatabaseBox.Text))
            {
                MessageBox.Show("Os campos Servidor e Banco de Dados são obrigatórios.");
                return;
            }
            
            // Preenche as propriedades públicas
            ServerName = ServerBox.Text.Trim();
            DatabaseName = DatabaseBox.Text.Trim();

            if (UseWindowsAuth.IsChecked == true)
            {
                ConnectionString = $"Server={ServerName};Database={DatabaseName};Integrated Security=True;TrustServerCertificate=True;";
            }
            else
            {
                string username = UsernameBox.Text.Trim();
                string password = PasswordBox.Password;
                if(string.IsNullOrWhiteSpace(username))
                {
                    MessageBox.Show("O campo Usuário é obrigatório para autenticação SQL.");
                    return;
                }
                ConnectionString = $"Server={ServerName};Database={DatabaseName};User Id={username};Password={password};TrustServerCertificate=True;";
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}