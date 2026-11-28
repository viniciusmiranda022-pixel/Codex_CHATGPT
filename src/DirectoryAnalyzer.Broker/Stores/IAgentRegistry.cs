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
        bool TryGetAgentByConnection(string connectionId, out AgentDescriptor agentInfo);
        IReadOnlyCollection<string> GetConnectionIdsForAgents(IReadOnlyCollection<string> agentIds);

        /// <summary>Conexões registradas com este AgentId. Nunca faz broadcast quando o ID é vazio.</summary>
        IReadOnlyCollection<string> GetConnectionIdsForAgent(string agentId);
        IReadOnlyCollection<string> GetAllConnectionIds();
    }
}
