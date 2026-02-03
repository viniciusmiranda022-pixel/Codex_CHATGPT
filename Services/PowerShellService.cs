using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;

namespace DirectoryAnalyzer.Services
{
    public class PowerShellService
    {
        public Task<IReadOnlyList<object>> ExecuteScriptAsync(string scriptText, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(scriptText))
            {
                throw new ArgumentException("O script PowerShell não pode estar vazio.", nameof(scriptText));
            }

            return Task.Run(() =>
            {
                using (var powerShell = PowerShell.Create())
                {
                    powerShell.AddScript(scriptText);

                    using (cancellationToken.Register(() =>
                           {
                               try
                               {
                                   powerShell.Stop();
                               }
                               catch (InvalidOperationException)
                               {
                               }
                           }))
                    {
                        var results = powerShell.Invoke();
                        if (powerShell.Streams.Error.Count > 0)
                        {
                            string errors = string.Join("; ", powerShell.Streams.Error.Select(e => e.ToString()));
                            throw new InvalidOperationException(errors);
                        }

                        return results.Cast<object>().ToList();
                    }
                }
            }, cancellationToken);
        }
    }
}
