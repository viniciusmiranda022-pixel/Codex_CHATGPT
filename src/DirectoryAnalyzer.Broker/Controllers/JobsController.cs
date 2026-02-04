using System.Threading.Tasks;
using DirectoryAnalyzer.Broker.Hubs;
using DirectoryAnalyzer.Broker.Stores;
using DirectoryAnalyzer.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace DirectoryAnalyzer.Broker.Controllers
{
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
            if (request == null)
            {
                return BadRequest();
            }

            var jobId = _jobStore.CreateJob(request);
            var connections = _agentRegistry.GetConnectionIdsForAgents(request.TargetAgentIds);
            foreach (var connectionId in connections)
            {
                await _hubContext.Clients.Client(connectionId).DispatchJob(jobId, request);
            }

            var status = _jobStore.GetStatus(jobId);
            return Ok(status);
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
