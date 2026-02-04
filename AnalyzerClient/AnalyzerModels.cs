using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace DirectoryAnalyzer.AnalyzerClient
{
    [DataContract]
    public sealed class AgentRequest
    {
        [DataMember(Order = 1)]
        public string RequestId { get; set; } = Guid.NewGuid().ToString();

        [DataMember(Order = 2)]
        public string ActionName { get; set; } = string.Empty;

        [DataMember(Order = 3)]
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    [DataContract]
    public sealed class AgentResponse
    {
        [DataMember(Order = 1)]
        public string RequestId { get; set; }

        [DataMember(Order = 2)]
        public string Status { get; set; }

        [DataMember(Order = 3)]
        public long DurationMs { get; set; }

        [DataMember(Order = 4)]
        public GetUsersResult Payload { get; set; }

        [DataMember(Order = 5, EmitDefaultValue = false)]
        public AgentError Error { get; set; }
    }

    [DataContract]
    public sealed class AgentError
    {
        [DataMember(Order = 1)]
        public string Code { get; set; }

        [DataMember(Order = 2)]
        public string Message { get; set; }

        [DataMember(Order = 3, EmitDefaultValue = false)]
        public string Details { get; set; }
    }

    [DataContract]
    public sealed class GetUsersResult
    {
        [DataMember(Order = 1)]
        public List<UserRecord> Users { get; set; } = new List<UserRecord>();
    }

    [DataContract]
    public sealed class UserRecord
    {
        [DataMember(Order = 1)]
        public string SamAccountName { get; set; }

        [DataMember(Order = 2)]
        public string DisplayName { get; set; }

        [DataMember(Order = 3)]
        public bool Enabled { get; set; }
    }
}
