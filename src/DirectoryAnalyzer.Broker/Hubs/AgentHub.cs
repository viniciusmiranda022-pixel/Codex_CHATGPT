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

        public override Task OnDisconnectedAsync(Exception exception)
        {
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
            if (status == null || !IsCallerTargetOf(jobId))
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
            // Só o agente designado no job pode entregar o resultado; os demais são ignorados.
            if (result == null || _jobStore.GetStatus(jobId) == null || !IsCallerTargetOf(jobId))
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

        private bool IsCallerTargetOf(string jobId)
        {
            var request = _jobStore.GetRequest(jobId);
            if (request?.TargetAgentIds == null || !_agentRegistry.TryGetAgentByConnection(Context.ConnectionId, out var caller))
            {
                return false;
            }

            return request.TargetAgentIds.Any(id => string.Equals(id, caller.AgentId, StringComparison.OrdinalIgnoreCase));
        }
    }
}
