using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace DirectoryAnalyzer.Agent
{
    public sealed class InventoryAgentService : ServiceBase
    {
        private readonly string _configPath;
        private CancellationTokenSource _cts;
        private AgentHost _host;

        public InventoryAgentService(string configPath)
        {
            _configPath = configPath;
            ServiceName = "DirectoryAnalyzerAgent";
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            _cts = new CancellationTokenSource();
            _host = new AgentHost(_configPath);
            _ = Task.Run(() => _host.StartAsync(_cts.Token));
        }

        protected override void OnStop()
        {
            _cts.Cancel();
            _host.StopAsync().GetAwaiter().GetResult();
        }
    }
}
