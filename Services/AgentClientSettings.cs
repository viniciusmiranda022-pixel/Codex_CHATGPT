using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace DirectoryAnalyzer.Services
{
    [DataContract]
    public sealed class AgentClientSettings
    {
        [DataMember(Order = 1)]
        public string AgentEndpoint { get; set; } = "https://localhost:8443/agent/";

        [DataMember(Order = 2)]
        public string ClientCertThumbprint { get; set; } = string.Empty;

        [DataMember(Order = 3)]
        public string[] AllowedAgentThumbprints { get; set; } = Array.Empty<string>();

        [DataMember(Order = 4)]
        public int RequestTimeoutSeconds { get; set; } = 30;
    }

    public static class AgentClientSettingsLoader
    {
        public static AgentClientSettings Load(string path)
        {
            using var stream = File.OpenRead(path);
            var serializer = new DataContractJsonSerializer(typeof(AgentClientSettings));
            var settings = serializer.ReadObject(stream) as AgentClientSettings;
            if (settings == null)
            {
                throw new InvalidOperationException("Invalid agent client settings.");
            }

            return settings;
        }
    }
}
