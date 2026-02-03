using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Threading;
using System.Threading.Tasks;

namespace DirectoryAnalyzer.Services
{
    public sealed class PowerShellExecutionResult
    {
        public PowerShellExecutionResult(IReadOnlyList<object> output, IReadOnlyList<string> errors)
            : this(output, errors, false, null, null, null)
        {
        }

        public PowerShellExecutionResult(
            IReadOnlyList<object> output,
            IReadOnlyList<string> errors,
            bool hadErrors,
            Exception exception,
            string script,
            IReadOnlyDictionary<string, object> parameters)
        {
            Output = output ?? Array.Empty<object>();
            Errors = errors ?? Array.Empty<string>();
            HadErrors = hadErrors;
            Exception = exception;
            Script = script ?? string.Empty;
            Parameters = parameters ?? new Dictionary<string, object>();
        }

        public IReadOnlyList<object> Output { get; }
        public IReadOnlyList<string> Errors { get; }
        public bool HasErrors => Errors.Count > 0;
        public bool HadErrors { get; }
        public Exception Exception { get; }
        public string Script { get; }
        public IReadOnlyDictionary<string, object> Parameters { get; }
    }

    public sealed class PowerShellExecutionException : InvalidOperationException
    {
        public PowerShellExecutionException(
            string message,
            Exception innerException,
            bool hadErrors,
            IReadOnlyList<string> errors,
            string script,
            IReadOnlyDictionary<string, object> parameters)
            : base(message, innerException)
        {
            HadErrors = hadErrors;
            Errors = errors ?? Array.Empty<string>();
            Script = script ?? string.Empty;
            Parameters = parameters ?? new Dictionary<string, object>();
        }

        public bool HadErrors { get; }
        public IReadOnlyList<string> Errors { get; }
        public string Script { get; }
        public IReadOnlyDictionary<string, object> Parameters { get; }
    }

    public class PowerShellService
    {
        private static readonly object ExecutionLock = new object();
        private static readonly string[] SensitiveParameterTokens =
        {
            "password",
            "passwd",
            "secret",
            "token",
            "apikey",
            "api_key",
            "credential",
            "cred"
        };

        public async Task<IReadOnlyList<object>> ExecuteScriptAsync(string scriptText, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(scriptText))
            {
                throw new ArgumentException("O script PowerShell não pode estar vazio.", nameof(scriptText));
            }

            var result = await ExecuteScriptWithResultAsync(scriptText, cancellationToken).ConfigureAwait(false);
            if (result.HasErrors || result.HadErrors)
            {
                string errors = string.Join("; ", result.Errors);
                throw new PowerShellExecutionException(
                    $"O script PowerShell retornou erros. {errors}",
                    result.Exception,
                    result.HadErrors,
                    result.Errors,
                    result.Script,
                    result.Parameters);
            }

            return result.Output;
        }

        public async Task<IReadOnlyList<object>> ExecuteScriptAsync(string scriptText, IDictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(scriptText))
            {
                throw new ArgumentException("O script PowerShell não pode estar vazio.", nameof(scriptText));
            }

            var result = await ExecuteScriptWithResultAsync(scriptText, parameters, cancellationToken).ConfigureAwait(false);
            if (result.HasErrors || result.HadErrors)
            {
                string errors = string.Join("; ", result.Errors);
                throw new PowerShellExecutionException(
                    $"O script PowerShell retornou erros. {errors}",
                    result.Exception,
                    result.HadErrors,
                    result.Errors,
                    result.Script,
                    result.Parameters);
            }

            return result.Output;
        }

        public Task<PowerShellExecutionResult> ExecuteScriptWithResultAsync(string scriptText, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(scriptText))
            {
                throw new ArgumentException("O script PowerShell não pode estar vazio.", nameof(scriptText));
            }

            return ExecuteScriptWithResultAsync(scriptText, null, cancellationToken);
        }

        public Task<PowerShellExecutionResult> ExecuteScriptWithResultAsync(string scriptText, IDictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(scriptText))
            {
                throw new ArgumentException("O script PowerShell não pode estar vazio.", nameof(scriptText));
            }

            return ExecuteScriptWithResultAsyncCore(scriptText, parameters, cancellationToken);
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

        private static Task<PowerShellExecutionResult> ExecuteScriptWithResultAsyncCore(
            string scriptText,
            IDictionary<string, object> parameters,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sanitizedScript = SanitizeScriptForLogging(scriptText);
                var sanitizedParameters = SanitizeParameters(parameters);

                lock (ExecutionLock)
                {
                    using (var runspace = RunspaceFactory.CreateRunspace())
                    using (var powerShell = PowerShell.Create())
                    {
                        runspace.Open();
                        powerShell.Runspace = runspace;
                        powerShell.AddScript(scriptText);
                        if (parameters != null && parameters.Count > 0)
                        {
                            foreach (var parameter in parameters)
                            {
                                powerShell.AddParameter(parameter.Key, parameter.Value);
                            }
                        }

                        List<object> output = new List<object>();
                        List<string> errors = new List<string>();
                        Exception executionException = null;
                        bool hadErrors = false;

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
                            try
                            {
                                var results = powerShell.Invoke();
                                output = results.Cast<object>().ToList();
                            }
                            catch (Exception ex) when (!(ex is OperationCanceledException))
                            {
                                executionException = ex;
                            }
                            finally
                            {
                                hadErrors = powerShell.HadErrors;
                                errors = powerShell.Streams.Error.Select(FormatErrorRecord).Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
                            }
                        }

                        if (executionException != null)
                        {
                            throw new PowerShellExecutionException(
                                "Falha ao executar script PowerShell.",
                                executionException,
                                hadErrors,
                                errors,
                                sanitizedScript,
                                sanitizedParameters);
                        }

                        if (cancellationToken.IsCancellationRequested)
                        {
                            throw new OperationCanceledException(cancellationToken);
                        }

                        return new PowerShellExecutionResult(output, errors, hadErrors, null, sanitizedScript, sanitizedParameters);
                    }
                }
            }, cancellationToken);
        }

        private static IReadOnlyDictionary<string, object> SanitizeParameters(IDictionary<string, object> parameters)
        {
            if (parameters == null || parameters.Count == 0)
            {
                return new Dictionary<string, object>();
            }

            var sanitized = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in parameters)
            {
                if (IsSensitiveToken(parameter.Key))
                {
                    sanitized[parameter.Key] = "***";
                }
                else
                {
                    sanitized[parameter.Key] = parameter.Value;
                }
            }

            return sanitized;
        }

        private static string SanitizeScriptForLogging(string scriptText)
        {
            if (string.IsNullOrWhiteSpace(scriptText))
            {
                return string.Empty;
            }

            string sanitized = scriptText;
            foreach (var token in SensitiveParameterTokens)
            {
                sanitized = System.Text.RegularExpressions.Regex.Replace(
                    sanitized,
                    $@"(?i)(-{token}\s+)(['""]?)([^'""]+)(\2)",
                    "$1$2***$2");
                sanitized = System.Text.RegularExpressions.Regex.Replace(
                    sanitized,
                    $@"(?i)({token}\s*=\s*)(['""]?)([^'""]+)(\2)",
                    "$1$2***$2");
            }

            return sanitized;
        }

        private static bool IsSensitiveToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            return SensitiveParameterTokens.Any(t => token.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string FormatErrorRecord(ErrorRecord record)
        {
            if (record == null)
            {
                return string.Empty;
            }

            string message = record.ToString();
            if (!string.IsNullOrWhiteSpace(record.FullyQualifiedErrorId))
            {
                message += $" | FullyQualifiedErrorId: {record.FullyQualifiedErrorId}";
            }

            if (record.CategoryInfo != null)
            {
                message += $" | Category: {record.CategoryInfo}";
            }

            if (record.Exception != null)
            {
                message += $" | Exception: {record.Exception}";
            }

            return message;
        }
    }
}
