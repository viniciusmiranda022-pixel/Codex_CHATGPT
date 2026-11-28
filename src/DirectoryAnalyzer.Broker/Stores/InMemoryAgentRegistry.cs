using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DirectoryAnalyzer.Contracts;

namespace DirectoryAnalyzer.Broker.Stores
{
    public sealed class InMemoryAgentRegistry : IAgentRegistry
    {
        private readonly ConcurrentDictionary<string, AgentDescriptor> _agents = new ConcurrentDictionary<string, AgentDescriptor>();

        public void Register(string connectionId, AgentDescriptor agentInfo)
        {
            _agents[connectionId] = agentInfo;
        }

        public void Unregister(string connectionId)
        {
            _agents.TryRemove(connectionId, out _);
        }

        public IReadOnlyCollection<AgentDescriptor> GetAgents()
        {
            return _agents.Values.ToList();
        }

        public bool TryGetAgent(string agentId, out AgentDescriptor agentInfo)
        {
            agentInfo = _agents.Values.FirstOrDefault(agent => agent.AgentId == agentId);
            return agentInfo != null;
        }

        public bool TryGetAgentByConnection(string connectionId, out AgentDescriptor agentInfo)
        {
            agentInfo = null;
            return !string.IsNullOrEmpty(connectionId) && _agents.TryGetValue(connectionId, out agentInfo) && agentInfo != null;
        }

        public IReadOnlyCollection<string> GetConnectionIdsForAgents(IReadOnlyCollection<string> agentIds)
        {
            if (agentIds == null || agentIds.Count == 0)
            {
                // Sem alvo não há destinatário: o broadcast entre ambientes foi removido.
                return new List<string>();
            }

            var connections = _agents
                .Where(pair => pair.Value != null && agentIds.Contains(pair.Value.AgentId))
                .Select(pair => pair.Key)
                .ToList();

            return connections;
        }

        public IReadOnlyCollection<string> GetConnectionIdsForAgent(string agentId)
        {
            if (string.IsNullOrWhiteSpace(agentId))
            {
                return new List<string>();
            }

            return _agents
                .Where(pair => pair.Value != null && string.Equals(pair.Value.AgentId, agentId, System.StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key)
                .ToList();
        }

        public IReadOnlyCollection<string> GetAllConnectionIds()
        {
            return _agents.Keys.ToList();
        }
    }
}
