using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DirectoryAnalyzer.Broker.Hubs;
using DirectoryAnalyzer.Broker.Stores;
using DirectoryAnalyzer.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace DirectoryAnalyzer.Broker.Controllers
{
    /// <summary>Corpo das recusas do Broker; o código permite à WPF distinguir a causa.</summary>
    public sealed class BrokerError
    {
        public BrokerError(string code, string message)
        {
            Code = code;
            Message = message;
        }

        public string Code { get; }
        public string Message { get; }
    }

    public static class BrokerErrorCodes
    {
        public const string InvalidRequest = "InvalidRequest";
        public const string InvalidTarget = "InvalidTarget";
        public const string AgentUnavailable = "AgentUnavailable";
        public const string AmbiguousTarget = "AmbiguousTarget";
    }

    [ApiController]
    [Route("api/jobs")]
    public sealed class JobsController : ControllerBase
    {
        private readonly IJobStore _jobStore;
        private readonly IResultStore _resultStore;
        private readonly IAgentRegistry _agentRegistry;
        private readonly IHubContext<AgentHub, IAgentClient> _hubContext;

        public JobsController(
            IJobStore jobStore,
            IResultStore resultStore,
            IAgentRegistry agentRegistry,
            IHubContext<AgentHub, IAgentClient> hubContext)
        {
            _jobStore = jobStore;
            _resultStore = resultStore;
            _agentRegistry = agentRegistry;
            _hubContext = hubContext;
        }

        [HttpPost]
        public async Task<IActionResult> CreateJob([FromBody] JobRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ModuleName))
            {
                return BadRequest(new BrokerError(BrokerErrorCodes.InvalidRequest, "Pedido sem módulo."));
            }

            // Origem e destino ficam separados: cada job vai para exatamente um agente nomeado.
            // Sem alvo, o registro devolveria todas as conexões (broadcast entre ambientes).
            var targets = request.TargetAgentIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
            if (targets.Count != 1)
            {
                return BadRequest(new BrokerError(BrokerErrorCodes.InvalidTarget, "Informe exatamente um agente alvo (TargetAgentIds)."));
            }

            request.TargetAgentIds = targets;
            var connections = _agentRegistry.GetConnectionIdsForAgent(targets[0]);
            if (connections.Count == 0)
            {
                return Conflict(new BrokerError(BrokerErrorCodes.AgentUnavailable, "O agente alvo não está conectado ao Broker."));
            }

            if (connections.Count > 1)
            {
                // Mais de uma conexão com o mesmo AgentId: não há como saber qual é a instalação legítima.
                // Nada é despachado até a ambiguidade ser resolvida (identidade por instalação: decisão 1).
                return Conflict(new BrokerError(BrokerErrorCodes.AmbiguousTarget, "Há mais de uma conexão registrada com este AgentId; o job não foi despachado."));
            }

            var connectionId = connections.First();
            var jobId = _jobStore.CreateJob(request, connectionId);

            // A conexão pode ter caído entre a seleção e a criação do job. O Hub retira a conexão do registro
            // antes de varrer os jobs dela; conferir de novo aqui fecha a janela: se ela sumiu, o job é
            // encerrado já (uma única vez, via TryFinish) e a WPF recebe a causa sem esperar o timeout.
            if (!IsStillConnected(connectionId, targets[0]))
            {
                _jobStore.TryFinish(jobId, JobState.Failed, AgentHub.AgentDisconnectedMessage);
                return Conflict(new BrokerError(BrokerErrorCodes.AgentUnavailable, "O agente alvo desconectou antes do despacho; o job foi encerrado."));
            }

            try
            {
                await _hubContext.Clients.Client(connectionId).DispatchJob(jobId, request);
            }
            catch (Exception)
            {
                _jobStore.TryFinish(jobId, JobState.Failed, AgentHub.AgentDisconnectedMessage);
                return Conflict(new BrokerError(BrokerErrorCodes.AgentUnavailable, "Falha ao entregar o job ao agente; o job foi encerrado."));
            }

            var status = _jobStore.GetStatus(jobId);
            return Ok(status);
        }

        private bool IsStillConnected(string connectionId, string agentId)
        {
            return _agentRegistry.TryGetAgentByConnection(connectionId, out var agent)
                && string.Equals(agent.AgentId, agentId, StringComparison.OrdinalIgnoreCase);
        }

        [HttpGet("{id}")]
        public IActionResult GetStatus(string id)
        {
            var status = _jobStore.GetStatus(id);
            if (status == null)
            {
                return NotFound();
            }

            return Ok(status);
        }

        [HttpGet("{id}/result")]
        public IActionResult GetResult(string id)
        {
            var result = _resultStore.GetResult(id);
            if (result == null)
            {
                return NotFound();
            }

            return Ok(result);
        }
    }
}
