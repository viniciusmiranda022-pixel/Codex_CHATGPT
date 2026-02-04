using System.Collections.Generic;

namespace DirectoryAnalyzer.Broker.Configuration
{
    public sealed class BrokerSettings
    {
        public bool EnableClientCertificateValidation { get; set; }
        public List<string> AllowedThumbprints { get; set; } = new List<string>();
        public List<string> TrustedCaThumbprints { get; set; } = new List<string>();
    }
}
