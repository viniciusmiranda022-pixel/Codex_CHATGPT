using System.Collections.Generic;
using DirectoryAnalyzer.Contracts;

namespace DirectoryAnalyzer.Broker.Stores
{
    public interface IJobStore
    {
        string CreateJob(JobRequest request);
        JobStatus GetStatus(string jobId);
        bool TryUpdateStatus(string jobId, JobStatus status);
        JobRequest GetRequest(string jobId);
        IReadOnlyCollection<string> ListJobIds();
    }
}
