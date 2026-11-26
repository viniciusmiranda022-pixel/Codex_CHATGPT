using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DirectoryAnalyzer.Contracts;

namespace DirectoryAnalyzer.Agent.Modules
{
    public sealed class ModuleExecutionOutcome
    {
        private ModuleExecutionOutcome(bool succeeded, IReadOnlyList<IReadOnlyDictionary<string, string>> rows, string errorCode, string errorMessage, string errorDetails)
        {
            Succeeded = succeeded;
            Rows = rows ?? Array.Empty<IReadOnlyDictionary<string, string>>();
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
            ErrorDetails = errorDetails;
        }

        public bool Succeeded { get; }
        public IReadOnlyList<IReadOnlyDictionary<string, string>> Rows { get; }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }
        public string ErrorDetails { get; }

        public static ModuleExecutionOutcome Success(IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
            => new ModuleExecutionOutcome(true, rows, null, null, null);

        public static ModuleExecutionOutcome Failure(string code, string message, string details = null)
            => new ModuleExecutionOutcome(false, null, code, message, details);
    }

    /// <summary>Executa um módulo já aprovado pelo gate, com parâmetros normalizados.</summary>
    public interface IModuleExecutor
    {
        Task<ModuleExecutionOutcome> ExecuteAsync(ModuleDefinition module, IReadOnlyDictionary<string, string> parameters, CancellationToken token);
    }

    /// <summary>
    /// Processa um job recebido do Broker: valida no gate, executa apenas se aceito e devolve sempre
    /// um ModuleResult (com Items ou com ErrorInfo).
    /// </summary>
    public sealed class AgentJobProcessor
    {
        public const string GateExceptionType = "AgentGate";
        public const string RejectedSummary = "Recusado pelo agente antes da execução";

        private readonly ModuleRequestGate _gate;
        private readonly IModuleExecutor _executor;
        private readonly AgentIdentity _identity;

        public AgentJobProcessor(ModuleCatalog catalog, IModuleExecutor executor, AgentIdentity identity)
        {
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
            _gate = new ModuleRequestGate(catalog ?? throw new ArgumentNullException(nameof(catalog)), identity);
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        public async Task<ModuleResult> ProcessAsync(string jobId, JobRequest request, Func<int, string, Task> reportProgress, CancellationToken token)
        {
            var startedAt = DateTime.UtcNow;
            var decision = _gate.Evaluate(request);
            var result = new ModuleResult
            {
                CorrelationId = string.IsNullOrWhiteSpace(request?.CorrelationId) ? jobId : request.CorrelationId,
                ModuleName = decision.Accepted ? decision.Module.Name : null,
                AgentId = _identity.AgentId,
                Host = _identity.Host,
                Domain = _identity.Domain,
                StartedAtUtc = startedAt
            };

            if (!decision.Accepted)
            {
                result.Summary = RejectedSummary;
                result.Errors.Add(new ErrorInfo
                {
                    Code = decision.ErrorCode,
                    Message = decision.ErrorMessage,
                    ExceptionType = GateExceptionType
                });
                result.CompletedAtUtc = DateTime.UtcNow;
                return result;
            }

            if (reportProgress != null)
            {
                await reportProgress(5, "Iniciado").ConfigureAwait(false);
            }

            ModuleExecutionOutcome outcome;
            try
            {
                outcome = await _executor.ExecuteAsync(decision.Module, decision.Parameters, token).ConfigureAwait(false)
                    ?? ModuleExecutionOutcome.Failure("ExecutionFailed", "O módulo não retornou resultado.");
            }
            catch (OperationCanceledException)
            {
                outcome = ModuleExecutionOutcome.Failure("Timeout", "A execução excedeu o tempo limite ou foi cancelada.");
            }
            catch (Exception ex)
            {
                outcome = ModuleExecutionOutcome.Failure("ExecutionFailed", "Falha na execução do módulo.", ex.GetType().Name);
            }

            if (outcome.Succeeded)
            {
                foreach (var row in outcome.Rows)
                {
                    var item = new ResultItem { Severity = ResultSeverity.Info };
                    foreach (var column in row)
                    {
                        item.Columns[column.Key] = column.Value ?? string.Empty;
                    }

                    result.Items.Add(item);
                }

                result.Summary = $"Itens: {result.Items.Count}";
            }
            else
            {
                result.Summary = "Falha na execução";
                result.Errors.Add(new ErrorInfo
                {
                    Code = outcome.ErrorCode,
                    Message = outcome.ErrorMessage,
                    Details = outcome.ErrorDetails,
                    ExceptionType = "AgentExecution"
                });
            }

            result.CompletedAtUtc = DateTime.UtcNow;
            return result;
        }
    }
}
