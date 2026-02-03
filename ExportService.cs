using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace DirectoryAnalyzer.Services
{
    public static class ExportService
    {
        public static void ExportToSql(IEnumerable<dynamic> data, string tableName, string connectionString)
        {
            try
            {
                using (var conn = new SqlConnection(connectionString))
                {
                    conn.Open();

                    // Converter lista dinâmica em DataTable
                    DataTable table = new DataTable();
                    foreach (var item in data)
                    {
                        var dict = (IDictionary<string, object>)item;
                        if (table.Columns.Count == 0)
                        {
                            foreach (var key in dict.Keys)
                                table.Columns.Add(key);
                        }
                        var row = table.NewRow();
                        foreach (var key in dict.Keys)
                            row[key] = dict[key] ?? DBNull.Value;
                        table.Rows.Add(row);
                    }

                    // Criar tabela no SQL se não existir
                    using (var createCmd = conn.CreateCommand())
                    {
                        List<string> columns = new List<string>();
                        foreach (DataColumn col in table.Columns)
                            columns.Add($"[{col.ColumnName}] NVARCHAR(MAX)");

                        createCmd.CommandText = $"IF OBJECT_ID('{tableName}') IS NULL CREATE TABLE [{tableName}] ({string.Join(",", columns)})";
                        createCmd.ExecuteNonQuery();
                    }

                    // Enviar dados para o SQL
                    using (var bulk = new SqlBulkCopy(conn))
                    {
                        bulk.DestinationTableName = tableName;
                        bulk.WriteToServer(table);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Erro ao exportar para SQL Server: " + ex.Message, ex);
            }
        }
    }
}
