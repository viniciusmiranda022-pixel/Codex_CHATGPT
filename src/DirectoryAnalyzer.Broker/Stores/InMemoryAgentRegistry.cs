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

        public IReadOnlyCollection<string> GetConnectionIdsForAgents(IReadOnlyCollection<string> agentIds)
        {
            if (agentIds == null || agentIds.Count == 0)
            {
                return GetAllConnectionIds();
            }

            var connections = _agents
                .Where(pair => pair.Value != null && agentIds.Contains(pair.Value.AgentId))
                .Select(pair => pair.Key)
                .ToList();

            return connections;
        }

        public IReadOnlyCollection<string> GetAllConnectionIds()
        {
            return _agents.Keys.ToList();
        }
    }
}
