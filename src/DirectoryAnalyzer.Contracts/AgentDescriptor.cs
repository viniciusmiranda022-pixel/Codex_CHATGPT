using System.Collections.Generic;

namespace DirectoryAnalyzer.Contracts
{
    public sealed class AgentDescriptor
    {
        public string ContractVersion { get; set; } = "1.0";
        public string AgentId { get; set; }
        public string Host { get; set; }
        public string Version { get; set; }
        public List<string> Capabilities { get; set; } = new List<string>();
    }
}
