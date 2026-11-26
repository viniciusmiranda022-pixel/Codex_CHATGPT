using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DirectoryAnalyzer.Agent.Modules;
using DirectoryAnalyzer.Contracts;

namespace DirectoryAnalyzer.Agent.Tests
{
    /// <summary>Dados fictícios. Nenhum nome, domínio ou host real.</summary>
    internal static class SyntheticData
    {
        public const string OriginAgentId = "agente-origem-01";
        public const string DestinationAgentId = "agente-destino-01";
        public const string OriginDomain = "origem.example";
        public const string DestinationDomain = "destino.example";

        public static AgentIdentity Origin => new AgentIdentity(OriginAgentId, "SRV-ORIGEM-01", OriginDomain);
        public static AgentIdentity Destination => new AgentIdentity(DestinationAgentId, "SRV-DESTINO-01", DestinationDomain);

        public static IReadOnlyList<IReadOnlyDictionary<string, string>> UsersFor(string domain)
        {
            return new List<IReadOnlyDictionary<string, string>>
            {
                new Dictionary<string, string> { ["SamAccountName"] = "usuario.ficticio01", ["DisplayName"] = "Usuário Fictício 01", ["Enabled"] = "True", ["UserPrincipalName"] = $"usuario.ficticio01@{domain}" },
                new Dictionary<string, string> { ["SamAccountName"] = "usuario.ficticio02", ["DisplayName"] = "Usuário Fictício 02", ["Enabled"] = "True", ["UserPrincipalName"] = $"usuario.ficticio02@{domain}" }
            };
        }

        public static JobRequest UsersRequest(string target, Dictionary<string, string> parameters = null)
        {
            return new JobRequest
            {
                CorrelationId = "corr-sintetico",
                ModuleName = "GetUsers",
                TargetAgentIds = new List<string> { target },
                Parameters = parameters ?? new Dictionary<string, string> { ["IncludeDisabled"] = "false" },
                RequestedBy = "operador.ficticio"
            };
        }
    }

    /// <summary>Executor sintético: registra chamadas e devolve usuários fictícios do domínio do agente.</summary>
    internal sealed class RecordingExecutor : IModuleExecutor
    {
        private readonly string _domain;
        private int _calls;

        public RecordingExecutor(string domain)
        {
            _domain = domain;
        }

        public int Calls => Volatile.Read(ref _calls);
        public TaskCompletionSource<bool> Gate { get; set; }
        public IReadOnlyDictionary<string, string> LastParameters { get; private set; }

        public async Task<ModuleExecutionOutcome> ExecuteAsync(ModuleDefinition module, IReadOnlyDictionary<string, string> parameters, CancellationToken token)
        {
            Interlocked.Increment(ref _calls);
            LastParameters = parameters;
            if (Gate != null)
            {
                await Gate.Task.ConfigureAwait(false);
            }

            return ModuleExecutionOutcome.Success(SyntheticData.UsersFor(_domain));
        }
    }
}
