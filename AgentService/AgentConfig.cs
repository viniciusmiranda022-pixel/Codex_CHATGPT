using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace DirectoryAnalyzer.Agent
{
    [DataContract]
    public sealed class AgentConfig
    {
        [DataMember(Order = 1)]
        public string BindPrefix { get; set; } = "https://+:8443/agent/";

        [DataMember(Order = 2)]
        public string CertThumbprint { get; set; } = string.Empty;

        [DataMember(Order = 3)]
        public string[] AnalyzerClientThumbprints { get; set; } = Array.Empty<string>();

        [DataMember(Order = 4)]
        public int ActionTimeoutSeconds { get; set; } = 30;

        [DataMember(Order = 5)]
        public string LogPath { get; set; } = @"C:\ProgramData\DirectoryAnalyzer\agent.log";

        [DataMember(Order = 6)]
        public string Domain { get; set; } = string.Empty;

        [DataMember(Order = 7)]
        public int MaxRequestBytes { get; set; } = 65536;
    }

    public static class ConfigLoader
    {
        public static AgentConfig Load(string path)
        {
            using var stream = File.OpenRead(path);
            var serializer = new DataContractJsonSerializer(typeof(AgentConfig));
            var config = serializer.ReadObject(stream) as AgentConfig;
            if (config == null)
            {
                throw new InvalidOperationException("Invalid agent configuration.");
            }

            return config;
        }
    }
}
