using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;

namespace DirectoryAnalyzer.Collectors
{
    public sealed class PowerShellCollector
    {
        public IReadOnlyList<Dictionary<string, string>> Execute(string scriptText, IDictionary<string, string> parameters, out IReadOnlyList<string> errors)
        {
            if (string.IsNullOrWhiteSpace(scriptText))
            {
                throw new ArgumentException("Script text cannot be empty.", nameof(scriptText));
            }

            using (var powerShell = PowerShell.Create())
            {
                powerShell.AddScript(scriptText);
                if (parameters != null && parameters.Count > 0)
                {
                    powerShell.AddParameters(parameters.ToDictionary(k => k.Key, v => (object)v.Value));
                }

                var output = powerShell.Invoke();
                errors = powerShell.Streams.Error.Select(err => err.ToString()).ToList();

                var results = new List<Dictionary<string, string>>();
                foreach (var item in output)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in item.Properties)
                    {
                        dict[prop.Name] = Convert.ToString(prop.Value) ?? string.Empty;
                    }

                    results.Add(dict);
                }

                return results;
            }
        }
    }
}
