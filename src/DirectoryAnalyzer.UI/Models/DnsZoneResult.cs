namespace DirectoryAnalyzer.Models
{
    public class DnsZoneResult
    {
        public string ZoneName { get; set; }
        public string ZoneType { get; set; }
        public string IsReverseLookupZone { get; set; }
        public string DynamicUpdate { get; set; }
    }
}
