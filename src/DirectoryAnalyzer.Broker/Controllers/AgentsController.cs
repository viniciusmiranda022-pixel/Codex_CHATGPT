using DirectoryAnalyzer.Broker.Stores;
using Microsoft.AspNetCore.Mvc;

namespace DirectoryAnalyzer.Broker.Controllers
{
    [ApiController]
    [Route("api/agents")]
    public sealed class AgentsController : ControllerBase
    {
        private readonly IAgentRegistry _agentRegistry;

        public AgentsController(IAgentRegistry agentRegistry)
        {
            _agentRegistry = agentRegistry;
        }

        [HttpGet]
        public IActionResult GetAgents()
        {
            return Ok(_agentRegistry.GetAgents());
        }
    }
}
