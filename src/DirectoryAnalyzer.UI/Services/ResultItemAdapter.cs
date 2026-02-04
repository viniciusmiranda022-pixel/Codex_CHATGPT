using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using DirectoryAnalyzer.Contracts;

namespace DirectoryAnalyzer.Services
{
    public static class ResultItemAdapter
    {
        public static IReadOnlyList<object> ToDisplayItems(IEnumerable<ResultItem> items)
        {
            if (items == null)
            {
                return new List<object>();
            }

            var list = new List<object>();
            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }

                IDictionary<string, object> expando = new ExpandoObject();
                foreach (var column in item.Columns)
                {
                    expando[column.Key] = column.Value;
                }

                if (!expando.ContainsKey("Severity"))
                {
                    expando["Severity"] = item.Severity.ToString();
                }

                if (!expando.ContainsKey("Notes") && !string.IsNullOrWhiteSpace(item.Notes))
                {
                    expando["Notes"] = item.Notes;
                }

                list.Add((ExpandoObject)expando);
            }

            return list;
        }
    }
}
