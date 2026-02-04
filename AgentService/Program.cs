using System;
using System.IO;
using System.ServiceProcess;

namespace DirectoryAnalyzer.Agent
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
 codex/design-production-grade-on-premises-agent-architecture-mn24bx
            var configPath = ResolveConfigPath("agentsettings.json");

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var configPath = Path.Combine(baseDir, "agentsettings.json");
 main

            if (Environment.UserInteractive)
            {
                var cts = new System.Threading.CancellationTokenSource();
                var host = new AgentHost(configPath);
                Console.WriteLine("Starting DirectoryAnalyzer agent in console mode...");
                Console.WriteLine("Press ENTER to stop.");
                _ = System.Threading.Tasks.Task.Run(() => host.StartAsync(cts.Token));
                Console.ReadLine();
                cts.Cancel();
                host.StopAsync().GetAwaiter().GetResult();
            }
            else
            {
                ServiceBase.Run(new InventoryAgentService(configPath));
            }
        }
 codex/design-production-grade-on-premises-agent-architecture-mn24bx

        private static string ResolveConfigPath(string fileName)
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var sharedPath = Path.Combine(programData, "DirectoryAnalyzer", fileName);
            if (File.Exists(sharedPath))
            {
                return sharedPath;
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
        }

 main
    }
}
