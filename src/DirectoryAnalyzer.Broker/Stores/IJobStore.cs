using System.Collections.Generic;
using DirectoryAnalyzer.Contracts;

namespace DirectoryAnalyzer.Broker.Stores
{
    public interface IJobStore
    {
        string CreateJob(JobRequest request);

        /// <summary>Cria o job já vinculado à única conexão autorizada a atualizá-lo.</summary>
        string CreateJob(JobRequest request, string assignedConnectionId);

        /// <summary>Conexão SignalR designada no despacho; null para jobs sem vínculo.</summary>
        string GetAssignedConnectionId(string jobId);
        JobStatus GetStatus(string jobId);
        bool TryUpdateStatus(string jobId, JobStatus status);
        JobRequest GetRequest(string jobId);
        IReadOnlyCollection<string> ListJobIds();
    }
}
