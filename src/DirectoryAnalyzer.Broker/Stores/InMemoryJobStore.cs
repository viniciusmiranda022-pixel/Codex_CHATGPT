using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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

            public object Sync { get; } = new object();
            public JobRequest Request { get; }
            public string AssignedConnectionId { get; }
            public JobStatus Status { get; set; }
        }

        private readonly ConcurrentDictionary<string, JobRecord> _jobs = new ConcurrentDictionary<string, JobRecord>();

        public static bool IsTerminal(JobState state) => state == JobState.Completed || state == JobState.Failed;

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
            if (jobId == null || !_jobs.TryGetValue(jobId, out var record))
            {
                return null;
            }

            lock (record.Sync)
            {
                return Clone(record.Status);
            }
        }

        public bool TryUpdateStatus(string jobId, JobStatus status)
        {
            if (jobId == null || status == null || !_jobs.TryGetValue(jobId, out var record))
            {
                return false;
            }

            lock (record.Sync)
            {
                if (IsTerminal(record.Status.State))
                {
                    // Estado terminal é definitivo; só TryFinish encerra o job.
                    return false;
                }

                record.Status = Clone(status);
                return true;
            }
        }

        public bool TryReportProgress(string jobId, int progressPercent, string message)
        {
            if (jobId == null || !_jobs.TryGetValue(jobId, out var record))
            {
                return false;
            }

            lock (record.Sync)
            {
                if (IsTerminal(record.Status.State))
                {
                    return false;
                }

                var now = DateTime.UtcNow;
                record.Status.ProgressPercent = progressPercent;
                record.Status.Message = message;
                record.Status.State = JobState.Running;
                record.Status.StartedAtUtc ??= now;
                record.Status.UpdatedAtUtc = now;
                return true;
            }
        }

        public bool TryFinish(string jobId, JobState terminalState, string message, Action beforeCommit = null)
        {
            if (!IsTerminal(terminalState))
            {
                throw new ArgumentOutOfRangeException(nameof(terminalState));
            }

            if (jobId == null || !_jobs.TryGetValue(jobId, out var record))
            {
                return false;
            }

            lock (record.Sync)
            {
                if (IsTerminal(record.Status.State))
                {
                    return false;
                }

                beforeCommit?.Invoke();
                var now = DateTime.UtcNow;
                record.Status.State = terminalState;
                record.Status.ProgressPercent = 100;
                record.Status.Message = message;
                record.Status.UpdatedAtUtc = now;
                record.Status.CompletedAtUtc = now;
                return true;
            }
        }

        public string GetAssignedConnectionId(string jobId)
        {
            return jobId != null && _jobs.TryGetValue(jobId, out var record) ? record.AssignedConnectionId : null;
        }

        public JobRequest GetRequest(string jobId)
        {
            return jobId != null && _jobs.TryGetValue(jobId, out var record) ? record.Request : null;
        }

        public IReadOnlyCollection<string> ListJobIds()
        {
            return _jobs.Keys.ToList();
        }

        public IReadOnlyCollection<string> ListOpenJobsForConnection(string connectionId)
        {
            return _jobs
                .Where(pair => string.Equals(pair.Value.AssignedConnectionId, connectionId, StringComparison.Ordinal))
                .Where(pair => { lock (pair.Value.Sync) { return !IsTerminal(pair.Value.Status.State); } })
                .Select(pair => pair.Key)
                .ToList();
        }

        private static JobStatus Clone(JobStatus status)
        {
            return new JobStatus
            {
                ContractVersion = status.ContractVersion,
                JobId = status.JobId,
                State = status.State,
                ProgressPercent = status.ProgressPercent,
                Message = status.Message,
                StartedAtUtc = status.StartedAtUtc,
                UpdatedAtUtc = status.UpdatedAtUtc,
                CompletedAtUtc = status.CompletedAtUtc,
                ErrorInfo = status.ErrorInfo
            };
        }
    }
}
