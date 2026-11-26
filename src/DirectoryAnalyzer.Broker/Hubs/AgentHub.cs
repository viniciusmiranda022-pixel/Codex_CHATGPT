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
            // Jobs vinculados a esta conexão não podem mais ser concluídos por outra; encerra como falha
            // para que a WPF mostre "agente indisponível" em vez de esperar indefinidamente.
            foreach (var jobId in _jobStore.ListJobIds())
            {
                if (!string.Equals(_jobStore.GetAssignedConnectionId(jobId), Context.ConnectionId, StringComparison.Ordinal))
                {
                    continue;
                }

                var status = _jobStore.GetStatus(jobId);
                if (status != null && status.State != JobState.Completed && status.State != JobState.Failed)
                {
                    status.State = JobState.Failed;
                    status.Message = AgentDisconnectedMessage;
                    status.UpdatedAtUtc = DateTime.UtcNow;
                    status.CompletedAtUtc = DateTime.UtcNow;
                    _jobStore.TryUpdateStatus(jobId, status);
                }
            }

            _agentRegistry.Unregister(Context.ConnectionId);
            return base.OnDisconnectedAsync(exception);
        }

        public Task AgentConnect(AgentDescriptor agentInfo)
        {
            _agentRegistry.Register(Context.ConnectionId, agentInfo);
            return Task.CompletedTask;
        }

        public Task ProgressUpdate(string jobId, int progressPercent, string message)
        {
            var status = _jobStore.GetStatus(jobId);
            if (status == null || !IsCallerAssignedTo(jobId))
            {
                return Task.CompletedTask;
            }

            status.ProgressPercent = progressPercent;
            status.Message = message;
            status.State = JobState.Running;
            status.UpdatedAtUtc = DateTime.UtcNow;
            _jobStore.TryUpdateStatus(jobId, status);
            return Task.CompletedTask;
        }

        public Task SubmitResult(string jobId, ModuleResult result)
        {
            // Só a conexão designada no despacho pode entregar o resultado; as demais são ignoradas,
            // inclusive outra conexão que declare o mesmo AgentId depois do despacho.
            if (result == null || _jobStore.GetStatus(jobId) == null || !IsCallerAssignedTo(jobId))
            {
                return Task.CompletedTask;
            }

            _agentRegistry.TryGetAgentByConnection(Context.ConnectionId, out var caller);
            result.AgentId = caller.AgentId;
            _resultStore.StoreResult(jobId, result);
            var status = _jobStore.GetStatus(jobId);
            if (status != null)
            {
                var failed = (result.Items == null || result.Items.Count == 0) && result.Errors != null && result.Errors.Count > 0;
                status.State = failed ? JobState.Failed : JobState.Completed;
                status.ProgressPercent = 100;
                status.Message = failed ? $"Falha: {result.Errors[0].Code}" : "Concluído";
                status.CompletedAtUtc = DateTime.UtcNow;
                status.UpdatedAtUtc = DateTime.UtcNow;
                _jobStore.TryUpdateStatus(jobId, status);
            }

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
