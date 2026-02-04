using System;
using System.Collections.Generic;

namespace DirectoryAnalyzer.Contracts
{
    public sealed class JobRequest
    {
        public string ContractVersion { get; set; } = "1.0";
        public string CorrelationId { get; set; }
        public string ModuleName { get; set; }
        public List<string> TargetAgentIds { get; set; } = new List<string>();
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
        public string RequestedBy { get; set; }
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    }
}
