using System;
using System.Collections.Generic;
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
    }
}
