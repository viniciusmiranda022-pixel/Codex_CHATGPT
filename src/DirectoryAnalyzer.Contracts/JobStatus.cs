using System;

namespace DirectoryAnalyzer.Contracts
{
    public sealed class JobStatus
    {
        public string ContractVersion { get; set; } = "1.0";
        public string JobId { get; set; }
        public JobState State { get; set; }
        public int ProgressPercent { get; set; }
        public string Message { get; set; }
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public ErrorInfo ErrorInfo { get; set; }
    }
}
