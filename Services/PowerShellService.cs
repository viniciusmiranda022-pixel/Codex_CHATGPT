using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.Linq;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;

namespace DirectoryAnalyzer.Services
{
    public sealed class PowerShellExecutionResult
    {
        public PowerShellExecutionResult(IReadOnlyList<object> output, IReadOnlyList<string> errors)
        {
            Output = output ?? Array.Empty<object>();
            Errors = errors ?? Array.Empty<string>();
        }

        public IReadOnlyList<object> Output { get; }
        public IReadOnlyList<string> Errors { get; }
        public bool HasErrors => Errors.Count > 0;
    }

    public class PowerShellService
    {
        private static readonly object ExecutionLock = new object();

        public Task<IReadOnlyList<object>> ExecuteScriptAsync(string scriptText, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(scriptText))
            {
                throw new ArgumentException("O script PowerShell não pode estar vazio.", nameof(scriptText));
            }

            return ExecuteScriptWithResultAsync(scriptText, cancellationToken).ContinueWith(task =>
            {
                if (task.IsFaulted && task.Exception != null)
                {
                    throw task.Exception.GetBaseException();
                }

                var result = task.Result;
                if (result.HasErrors)
                {
                    string errors = string.Join("; ", result.Errors);
                    throw new InvalidOperationException(errors);
                }

                return result.Output;
            }, cancellationToken);
        }

        public Task<IReadOnlyList<object>> ExecuteScriptAsync(string scriptText, IDictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(scriptText))
            {
                throw new ArgumentException("O script PowerShell não pode estar vazio.", nameof(scriptText));
            }

            return ExecuteScriptWithResultAsync(scriptText, parameters, cancellationToken).ContinueWith(task =>
            {
                if (task.IsFaulted && task.Exception != null)
                {
                    throw task.Exception.GetBaseException();
                }

                var result = task.Result;
                if (result.HasErrors)
                {
                    string errors = string.Join("; ", result.Errors);
                    throw new InvalidOperationException(errors);
                }

                return result.Output;
            }, cancellationToken);
        }

        public Task<PowerShellExecutionResult> ExecuteScriptWithResultAsync(string scriptText, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(scriptText))
            {
                throw new ArgumentException("O script PowerShell não pode estar vazio.", nameof(scriptText));
            }

            return Task.Run(() =>
            {
                lock (ExecutionLock)
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
                            var output = results.Cast<object>().ToList();
                            var errors = powerShell.Streams.Error.Select(e => e.ToString()).ToList();
                            return new PowerShellExecutionResult(output, errors);
                        }
                    }
                }
            }, cancellationToken);
        }

        public Task<PowerShellExecutionResult> ExecuteScriptWithResultAsync(string scriptText, IDictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(scriptText))
            {
                throw new ArgumentException("O script PowerShell não pode estar vazio.", nameof(scriptText));
            }

            return Task.Run(() =>
            {
                lock (ExecutionLock)
                {
                    using (var powerShell = PowerShell.Create())
                    {
                        powerShell.AddScript(scriptText);
                        if (parameters != null && parameters.Count > 0)
                        {
if (parameters != null && parameters.Count > 0)
{
    powerShell.AddParameters(parameters);
}

                        }

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
                            var output = results.Cast<object>().ToList();
                            var errors = powerShell.Streams.Error.Select(e => e.ToString()).ToList();
                            return new PowerShellExecutionResult(output, errors);
                        }
                    }
                }
            }, cancellationToken);
        }

        public string GetDomainNetBiosName()
        {
            try
            {
                using (var rootDse = new DirectoryEntry("LDAP://RootDSE"))
                {
                    var defaultNamingContext = rootDse.Properties["defaultNamingContext"]?.Value as string;
                    var configurationNamingContext = rootDse.Properties["configurationNamingContext"]?.Value as string;

                    if (string.IsNullOrWhiteSpace(defaultNamingContext) || string.IsNullOrWhiteSpace(configurationNamingContext))
                    {
                        return string.Empty;
                    }

                    using (var partitions = new DirectoryEntry($"LDAP://CN=Partitions,{configurationNamingContext}"))
                    using (var searcher = new DirectorySearcher(partitions))
                    {
                        searcher.Filter = $"(&(objectClass=crossRef)(nCName={EscapeLdapFilterValue(defaultNamingContext)}))";
                        searcher.PropertiesToLoad.Add("nETBIOSName");
                        var result = searcher.FindOne();
                        if (result != null && result.Properties["nETBIOSName"].Count > 0)
                        {
                            return result.Properties["nETBIOSName"][0]?.ToString() ?? string.Empty;
                        }
                    }
                }
            }
            catch
            {
                return string.Empty;
            }

            return string.Empty;
        }

        private static string EscapeLdapFilterValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\\", "\\5c")
                .Replace("*", "\\2a")
                .Replace("(", "\\28")
                .Replace(")", "\\29")
                .Replace("\0", "\\00");
        }
    }
}
