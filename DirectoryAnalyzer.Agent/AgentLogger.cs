using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace DirectoryAnalyzer.Agent
{
    public sealed class AgentLogger
    {
        private readonly string _logPath;
        private readonly object _sync = new object();

        public AgentLogger(string logPath)
        {
            _logPath = logPath;
            var dir = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        public void Write(AgentLogEntry entry)
        {
            var serializer = new DataContractJsonSerializer(typeof(AgentLogEntry));
            using var buffer = new MemoryStream();
            serializer.WriteObject(buffer, entry);
            var json = Encoding.UTF8.GetString(buffer.ToArray());

            lock (_sync)
            {
                File.AppendAllText(_logPath, json + Environment.NewLine, Encoding.UTF8);
            }
        }

        public static AgentLogEntry Create(
            string requestId,
            string actionName,
            string clientThumbprint,
            string clientSubject,
            long durationMs,
            string status,
            string errorCode,
            string message = null)
        {
            return new AgentLogEntry
            {
                TimestampUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                RequestId = requestId,
                ActionName = actionName,
                ClientThumbprint = clientThumbprint,
                ClientSubject = clientSubject,
                DurationMs = durationMs,
                Status = status,
                ErrorCode = errorCode,
                Message = message
            };
        }
    }

    [System.Runtime.Serialization.DataContract]
    public sealed class AgentLogEntry
    {
        [System.Runtime.Serialization.DataMember(Order = 1)]
        public string TimestampUtc { get; set; }

        [System.Runtime.Serialization.DataMember(Order = 2)]
        public string RequestId { get; set; }

        [System.Runtime.Serialization.DataMember(Order = 3)]
        public string ActionName { get; set; }

        [System.Runtime.Serialization.DataMember(Order = 4)]
        public string ClientThumbprint { get; set; }

        [System.Runtime.Serialization.DataMember(Order = 5)]
        public string ClientSubject { get; set; }

        [System.Runtime.Serialization.DataMember(Order = 6)]
        public long DurationMs { get; set; }

        [System.Runtime.Serialization.DataMember(Order = 7)]
        public string Status { get; set; }

        [System.Runtime.Serialization.DataMember(Order = 8, EmitDefaultValue = false)]
        public string ErrorCode { get; set; }

        [System.Runtime.Serialization.DataMember(Order = 9, EmitDefaultValue = false)]
        public string Message { get; set; }
    }
}
