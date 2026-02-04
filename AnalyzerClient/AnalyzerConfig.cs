using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace DirectoryAnalyzer.AnalyzerClient
{
    [DataContract]
    public sealed class AnalyzerConfig
    {
        [DataMember(Order = 1)]
        public string AgentEndpoint { get; set; } = "https://localhost:8443/agent/";

        [DataMember(Order = 2)]
        public string ClientCertThumbprint { get; set; } = string.Empty;

        [DataMember(Order = 3)]
        public string[] AllowedAgentThumbprints { get; set; } = Array.Empty<string>();

        [DataMember(Order = 4)]
        public int RequestTimeoutSeconds { get; set; } = 30;

        [DataMember(Order = 5)]
        public bool EnforceRevocationCheck { get; set; } = true;

        [DataMember(Order = 6)]
        public bool FailOpenOnRevocation { get; set; } = false;
    }

    public static class AnalyzerConfigLoader
    {
        public static AnalyzerConfig Load(string path)
        {
            using var stream = File.OpenRead(path);
            var serializer = new DataContractJsonSerializer(typeof(AnalyzerConfig));
            var config = serializer.ReadObject(stream) as AnalyzerConfig;
            if (config == null)
            {
                throw new InvalidOperationException("Invalid analyzer configuration.");
            }

            return config;
        }

        public static bool TryLoad(string path, out AnalyzerConfig config, out string error)
        {
            try
            {
                config = Load(path);
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                config = null;
                error = ex.Message;
                return false;
            }
        }
    }
}
