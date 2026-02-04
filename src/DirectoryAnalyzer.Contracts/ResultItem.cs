using System.Collections.Generic;

namespace DirectoryAnalyzer.Contracts
{
    public sealed class ResultItem
    {
        public string ContractVersion { get; set; } = "1.0";
        public Dictionary<string, string> Columns { get; set; } = new Dictionary<string, string>();
        public ResultSeverity Severity { get; set; }
        public string Notes { get; set; }
    }
}
