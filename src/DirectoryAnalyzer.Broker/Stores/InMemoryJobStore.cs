using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using DirectoryAnalyzer.Contracts;

namespace DirectoryAnalyzer.Broker.Stores
{
    public sealed class InMemoryJobStore : IJobStore
    {
        private sealed class JobRecord
        {
            public JobRecord(JobRequest request, JobStatus status, string assignedConnectionId)
            {
                Request = request;
                Status = status;
                AssignedConnectionId = assignedConnectionId;
            }

            public JobRequest Request { get; }
            public string AssignedConnectionId { get; }
            public JobStatus Status { get; set; }
        }

        private readonly ConcurrentDictionary<string, JobRecord> _jobs = new ConcurrentDictionary<string, JobRecord>();

        public string CreateJob(JobRequest request)
        {
            return CreateJob(request, null);
        }

        public string CreateJob(JobRequest request, string assignedConnectionId)
        {
            var jobId = Guid.NewGuid().ToString("N");
            var status = new JobStatus
            {
                JobId = jobId,
                State = JobState.Pending,
                ProgressPercent = 0,
                Message = "Job criado",
                StartedAtUtc = null,
                UpdatedAtUtc = DateTime.UtcNow
            };

            _jobs[jobId] = new JobRecord(request, status, assignedConnectionId);
            return jobId;
        }

        public JobStatus GetStatus(string jobId)
        {
            return _jobs.TryGetValue(jobId, out var record) ? record.Status : null;
        }

        public bool TryUpdateStatus(string jobId, JobStatus status)
        {
            if (_jobs.TryGetValue(jobId, out var record))
            {
                record.Status = status;
                return true;
            }

            return false;
        }

        public string GetAssignedConnectionId(string jobId)
        {
            return _jobs.TryGetValue(jobId, out var record) ? record.AssignedConnectionId : null;
        }

        public JobRequest GetRequest(string jobId)
        {
            return _jobs.TryGetValue(jobId, out var record) ? record.Request : null;
        }

        public IReadOnlyCollection<string> ListJobIds()
        {
            return _jobs.Keys as IReadOnlyCollection<string> ?? new List<string>(_jobs.Keys);
        }
    }
}
