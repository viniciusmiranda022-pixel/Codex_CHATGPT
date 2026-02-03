using System;
using System.Data;
using System.Data.SqlClient;

namespace DirectoryAnalyzer.Services
{
    public sealed class SqlManagerService
    {
        private readonly string _serverName;
        private readonly string _databaseName;
        private readonly string _connectionString;

        public SqlManagerService(string serverName, string databaseName, string connectionString)
        {
            _serverName = serverName ?? string.Empty;
            _databaseName = databaseName ?? string.Empty;
            _connectionString = connectionString ?? string.Empty;
        }

        public void EnsureDatabaseExists()
        {
            if (string.IsNullOrWhiteSpace(_databaseName))
            {
                throw new InvalidOperationException("O nome do banco de dados não pode estar vazio.");
            }

            var builder = string.IsNullOrWhiteSpace(_connectionString)
                ? new SqlConnectionStringBuilder { DataSource = _serverName, InitialCatalog = "master", IntegratedSecurity = true, TrustServerCertificate = true }
                : new SqlConnectionStringBuilder(_connectionString);
            builder.InitialCatalog = "master";

            using (var connection = new SqlConnection(builder.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                command.CommandType = CommandType.Text;
                command.CommandText = @"
DECLARE @dbName sysname = @DatabaseName;
IF DB_ID(@dbName) IS NULL
BEGIN
    EXEC('CREATE DATABASE ' + QUOTENAME(@dbName));
END";
                command.Parameters.AddWithValue("@DatabaseName", _databaseName);

                connection.Open();
                command.ExecuteNonQuery();
            }
        }
    }
}
