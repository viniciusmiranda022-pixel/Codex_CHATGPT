using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace DirectoryAnalyzer.Services
{
    [DataContract]
    public sealed class BrokerClientSettings
    {
        [DataMember(Order = 1)]
        public string BrokerBaseUrl { get; set; } = "https://localhost:5001";

        [DataMember(Order = 2)]
        public string ClientCertThumbprint { get; set; } = string.Empty;

        [DataMember(Order = 3)]
        public int RequestTimeoutSeconds { get; set; } = 30;

        [DataMember(Order = 4)]
        public int PollIntervalSeconds { get; set; } = 2;
    }

    public static class BrokerClientSettingsLoader
    {
        public static BrokerClientSettings Load(string path)
        {
            using var stream = File.OpenRead(path);
            var serializer = new DataContractJsonSerializer(typeof(BrokerClientSettings));
            var settings = serializer.ReadObject(stream) as BrokerClientSettings;
            if (settings == null)
            {
                throw new InvalidOperationException("Invalid broker client settings.");
            }

            return settings;
        }
    }
}
