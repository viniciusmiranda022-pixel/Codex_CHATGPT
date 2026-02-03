using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace DirectoryAnalyzer.Services
{
    public static class ExportService
    {
        public static void ExportToCsv(IEnumerable<object> data, string filePath)
        {
            var items = NormalizeItems(data);
            if (items.Count == 0)
            {
                return;
            }

            var properties = GetReadableProperties(items[0].GetType());
            using (var writer = new StreamWriter(filePath, false, new UTF8Encoding(true)))
            {
                writer.WriteLine(string.Join(";", properties.Select(p => EscapeCsv(p.Name))));
                foreach (var item in items)
                {
                    var values = properties.Select(p => EscapeCsv(Convert.ToString(p.GetValue(item), CultureInfo.InvariantCulture)));
                    writer.WriteLine(string.Join(";", values));
                }
            }
        }

        public static void ExportToXml(IEnumerable<object> data, string filePath, string rootName)
        {
            var items = NormalizeItems(data);
            if (items.Count == 0)
            {
                return;
            }

            var properties = GetReadableProperties(items[0].GetType());
            using (var writer = new XmlTextWriter(filePath, new UTF8Encoding(true)))
            {
                writer.Formatting = Formatting.Indented;
                writer.WriteStartDocument();
                writer.WriteStartElement(SanitizeXmlElementName(rootName));

                foreach (var item in items)
                {
                    writer.WriteStartElement("Item");
                    foreach (var prop in properties)
                    {
                        var value = Convert.ToString(prop.GetValue(item), CultureInfo.InvariantCulture) ?? string.Empty;
                        writer.WriteElementString(SanitizeXmlElementName(prop.Name), value);
                    }
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
        }

        public static void ExportToHtml(IEnumerable<object> data, string filePath, string title)
        {
            var items = NormalizeItems(data);
            if (items.Count == 0)
            {
                return;
            }

            var properties = GetReadableProperties(items[0].GetType());
            var sb = new StringBuilder();
            sb.AppendLine("<html><head><meta charset='UTF-8'/>");
            sb.AppendLine($"<title>{System.Security.SecurityElement.Escape(title)}</title>");
            sb.AppendLine("<style>body{font-family:sans-serif}table{border-collapse:collapse;width:100%}td,th{border:1px solid #ddd;padding:8px}tr:nth-child(even){background-color:#f2f2f2}</style>");
            sb.AppendLine("</head><body>");
            sb.AppendLine($"<h2>{System.Security.SecurityElement.Escape(title)}</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("<tr>");
            foreach (var prop in properties)
            {
                sb.Append($"<th>{System.Security.SecurityElement.Escape(prop.Name)}</th>");
            }
            sb.AppendLine("</tr>");

            foreach (var item in items)
            {
                sb.AppendLine("<tr>");
                foreach (var prop in properties)
                {
                    var value = Convert.ToString(prop.GetValue(item), CultureInfo.InvariantCulture) ?? string.Empty;
                    sb.Append($"<td>{System.Security.SecurityElement.Escape(value)}</td>");
                }
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</table></body></html>");
            File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
        }

        public static void ExportToSql(IEnumerable<object> data, string tableName, string connectionString)
        {
            try
            {
                var items = NormalizeItems(data);
                if (items.Count == 0)
                {
                    return;
                }

                string sanitizedTableName = SanitizeSqlIdentifier(tableName, "Export");
                using (var conn = new SqlConnection(connectionString))
                {
                    conn.Open();

                    // Converter lista dinâmica em DataTable
                    DataTable table = new DataTable();
                    var properties = GetReadableProperties(items[0].GetType());
                    foreach (var prop in properties)
                    {
                        string columnName = SanitizeSqlIdentifier(prop.Name, "Column");
                        if (!table.Columns.Contains(columnName))
                        {
                            table.Columns.Add(columnName);
                        }
                    }

                    foreach (var item in items)
                    {
                        var row = table.NewRow();
                        foreach (var prop in properties)
                        {
                            string columnName = SanitizeSqlIdentifier(prop.Name, "Column");
                            row[columnName] = prop.GetValue(item) ?? DBNull.Value;
                        }
                        table.Rows.Add(row);
                    }

                    // Criar tabela no SQL se não existir
                    using (var createCmd = conn.CreateCommand())
                    {
                        List<string> columns = new List<string>();
                        foreach (DataColumn col in table.Columns)
                            columns.Add($"[{col.ColumnName}] NVARCHAR(MAX)");

                        createCmd.CommandText = $"IF OBJECT_ID('{sanitizedTableName}') IS NULL CREATE TABLE [{sanitizedTableName}] ({string.Join(",", columns)})";
                        createCmd.ExecuteNonQuery();
                    }

                    // Enviar dados para o SQL
                    using (var bulk = new SqlBulkCopy(conn))
                    {
                        bulk.DestinationTableName = sanitizedTableName;
                        bulk.WriteToServer(table);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Erro ao exportar para SQL Server: " + ex.Message, ex);
            }
        }

        private static List<object> NormalizeItems(IEnumerable<object> data)
        {
            return data?.Where(item => item != null).ToList() ?? new List<object>();
        }

        private static System.Reflection.PropertyInfo[] GetReadableProperties(Type type)
        {
            return type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(p => p.CanRead)
                .ToArray();
        }

        private static string EscapeCsv(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            bool mustQuote = value.Contains(";") || value.Contains("\"") || value.Contains("\r") || value.Contains("\n");
            string escaped = value.Replace("\"", "\"\"");
            return mustQuote ? $"\"{escaped}\"" : escaped;
        }

        private static string SanitizeSqlIdentifier(string identifier, string fallback)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return fallback;
            }

            var builder = new StringBuilder(identifier.Length);
            foreach (char ch in identifier)
            {
                builder.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
            }

            string sanitized = builder.ToString().Trim('_');
            return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
        }

        private static string SanitizeXmlElementName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Item";
            }

            var builder = new StringBuilder();
            foreach (char ch in name)
            {
                builder.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
            }

            string sanitized = builder.ToString();
            if (!char.IsLetter(sanitized[0]) && sanitized[0] != '_')
            {
                sanitized = "_" + sanitized;
            }
            return sanitized;
        }
    }
}
