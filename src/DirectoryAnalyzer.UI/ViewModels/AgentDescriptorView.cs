using System.Linq;
using DirectoryAnalyzer.Contracts;

namespace DirectoryAnalyzer.ViewModels
{
    public sealed class AgentDescriptorView
    {
        public AgentDescriptorView(AgentDescriptor descriptor)
        {
            AgentId = descriptor?.AgentId;
            Host = descriptor?.Host;
            Version = descriptor?.Version;
            CapabilitiesDisplay = descriptor?.Capabilities == null
                ? string.Empty
                : string.Join(", ", descriptor.Capabilities.Where(cap => !string.IsNullOrWhiteSpace(cap)));
        }

        public string AgentId { get; }
        public string Host { get; }
        public string Version { get; }
        public string CapabilitiesDisplay { get; }
    }
}
