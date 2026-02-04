using System;
using System.Collections.Generic;

namespace DirectoryAnalyzer.Contracts
{
    public sealed class ModuleResult
    {
        public string ContractVersion { get; set; } = "1.0";
        public string CorrelationId { get; set; }
        public string ModuleName { get; set; }
        public string AgentId { get; set; }
        public string Host { get; set; }
        public string Domain { get; set; }
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public List<ResultItem> Items { get; set; } = new List<ResultItem>();
        public string Summary { get; set; }
        public List<ErrorInfo> Errors { get; set; } = new List<ErrorInfo>();
    }
}
