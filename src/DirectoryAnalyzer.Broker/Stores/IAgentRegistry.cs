using System.Collections.Generic;
using DirectoryAnalyzer.Contracts;

namespace DirectoryAnalyzer.Broker.Stores
{
    public interface IAgentRegistry
    {
        void Register(string connectionId, AgentDescriptor agentInfo);
        void Unregister(string connectionId);
        IReadOnlyCollection<AgentDescriptor> GetAgents();
        bool TryGetAgent(string agentId, out AgentDescriptor agentInfo);
        IReadOnlyCollection<string> GetConnectionIdsForAgents(IReadOnlyCollection<string> agentIds);
        IReadOnlyCollection<string> GetAllConnectionIds();
    }
}
