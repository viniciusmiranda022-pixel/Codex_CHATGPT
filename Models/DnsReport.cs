using System.Collections.Generic;

namespace DirectoryAnalyzer.Models
{
    public class DnsReport
    {
        public List<DnsZoneResult> Zones { get; } = new List<DnsZoneResult>();
        public List<DnsRecordResult> Records { get; } = new List<DnsRecordResult>();
        public List<DnsForwarderResult> Forwarders { get; } = new List<DnsForwarderResult>();
    }
}
