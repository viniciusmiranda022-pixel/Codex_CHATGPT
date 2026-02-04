using System;
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
            if (status == null)
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
            _resultStore.StoreResult(jobId, result);
            var status = _jobStore.GetStatus(jobId);
            if (status != null)
            {
                status.State = JobState.Completed;
                status.ProgressPercent = 100;
                status.Message = "Concluído";
                status.CompletedAtUtc = DateTime.UtcNow;
                status.UpdatedAtUtc = DateTime.UtcNow;
                _jobStore.TryUpdateStatus(jobId, status);
            }

            return Task.CompletedTask;
        }
    }
}
