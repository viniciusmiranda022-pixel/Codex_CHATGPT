using System;
using System.Linq;
using System.Threading.Tasks;
using DirectoryAnalyzer.Broker.Stores;
using DirectoryAnalyzer.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace DirectoryAnalyzer.Broker.Hubs
{
    public sealed class AgentHub : Hub<IAgentClient>
    {
        private readonly IJobStore _jobStore;
        private readonly IResultStore _resultStore;
        private readonly IAgentRegistry _agentRegistry;

        public AgentHub(IJobStore jobStore, IResultStore resultStore, IAgentRegistry agentRegistry)
        {
            _jobStore = jobStore;
            _resultStore = resultStore;
            _agentRegistry = agentRegistry;
        }

        public const string AgentDisconnectedMessage = "AgentDisconnected: a conexão do agente caiu antes da entrega do resultado.";

        public override Task OnDisconnectedAsync(Exception exception)
        {
            // Ordem importa: primeiro a conexão sai do registro, depois os jobs dela são encerrados.
            // Assim, um despacho concorrente ou já vê a conexão ausente (e o próprio controller encerra o
            // job) ou criou o job antes desta varredura (e ela o encerra). TryFinish garante um único fim.
            _agentRegistry.Unregister(Context.ConnectionId);
            FailOpenJobs(_jobStore, Context.ConnectionId);
            return base.OnDisconnectedAsync(exception);
        }

        /// <summary>Encerra como falha os jobs abertos vinculados a uma conexão que não existe mais.</summary>
        public static void FailOpenJobs(IJobStore jobStore, string connectionId)
        {
            foreach (var jobId in jobStore.ListOpenJobsForConnection(connectionId))
            {
                jobStore.TryFinish(jobId, JobState.Failed, AgentDisconnectedMessage);
            }
        }

        public Task AgentConnect(AgentDescriptor agentInfo)
        {
            _agentRegistry.Register(Context.ConnectionId, agentInfo);
            return Task.CompletedTask;
        }

        public Task ProgressUpdate(string jobId, int progressPercent, string message)
        {
            if (!IsCallerAssignedTo(jobId))
            {
                return Task.CompletedTask;
            }

            // Progresso tardio não reabre um job já encerrado.
            _jobStore.TryReportProgress(jobId, progressPercent, message);
            return Task.CompletedTask;
        }

        public Task SubmitResult(string jobId, ModuleResult result)
        {
            // Só a conexão designada no despacho pode entregar o resultado; as demais são ignoradas,
            // inclusive outra conexão que declare o mesmo AgentId depois do despacho.
            if (result == null || !IsCallerAssignedTo(jobId)
                || !_agentRegistry.TryGetAgentByConnection(Context.ConnectionId, out var caller))
            {
                return Task.CompletedTask;
            }

            result.AgentId = caller.AgentId;
            var failed = (result.Items == null || result.Items.Count == 0) && result.Errors != null && result.Errors.Count > 0;
            _jobStore.TryFinish(
                jobId,
                failed ? JobState.Failed : JobState.Completed,
                failed ? $"Falha: {result.Errors[0].Code}" : "Concluído",
                // O resultado fica disponível antes de o estado terminal ser visível à WPF, e só se este
                // for o primeiro encerramento (um job já encerrado por desconexão não recebe resultado).
                () => _resultStore.StoreResult(jobId, result));
            return Task.CompletedTask;
        }

        private bool IsCallerAssignedTo(string jobId)
        {
            var assigned = _jobStore.GetAssignedConnectionId(jobId);
            if (string.IsNullOrEmpty(assigned) || !string.Equals(assigned, Context.ConnectionId, StringComparison.Ordinal))
            {
                return false;
            }

            var request = _jobStore.GetRequest(jobId);
            if (request?.TargetAgentIds == null || request.TargetAgentIds.Count != 1
                || !_agentRegistry.TryGetAgentByConnection(Context.ConnectionId, out var caller))
            {
                return false;
            }

            return string.Equals(request.TargetAgentIds[0], caller.AgentId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
