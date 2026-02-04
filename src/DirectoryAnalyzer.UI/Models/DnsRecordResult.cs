namespace DirectoryAnalyzer.Models
{
    public class DnsRecordResult
    {
        public string ZoneName { get; set; }
        public string HostName { get; set; }
        public string RecordType { get; set; }
        public string TimeToLive { get; set; }
        public string RecordData { get; set; }
    }
}
