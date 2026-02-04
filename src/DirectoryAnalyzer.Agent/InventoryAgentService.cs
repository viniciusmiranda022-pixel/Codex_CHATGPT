using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace DirectoryAnalyzer.Agent
{
    public sealed class InventoryAgentService : ServiceBase
    {
        private readonly AgentConfig _config;
        private CancellationTokenSource _cts;
        private BrokerAgentWorker _worker;

        public InventoryAgentService(AgentConfig config)
        {
            _config = config;
            ServiceName = "DirectoryAnalyzerAgent";
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            _cts = new CancellationTokenSource();
            _worker = new BrokerAgentWorker(_config);
            _ = Task.Run(() => _worker.StartAsync(_cts.Token));
        }

        protected override void OnStop()
        {
            _cts.Cancel();
            _worker.StopAsync().GetAwaiter().GetResult();
        }
    }
}
