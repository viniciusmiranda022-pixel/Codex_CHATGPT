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

        /// <summary>
        /// Agente que receberá as coletas (AgentId exibido em Agentes). O Broker exige exatamente um alvo
        /// por job, para que uma coleta da origem não seja executada no destino.
        /// </summary>
        [DataMember(Order = 5)]
        public string TargetAgentId { get; set; } = string.Empty;
    }
}
