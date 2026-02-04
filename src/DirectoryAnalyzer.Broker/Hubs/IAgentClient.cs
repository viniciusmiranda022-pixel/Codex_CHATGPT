using System.Threading.Tasks;
using DirectoryAnalyzer.Contracts;

namespace DirectoryAnalyzer.Broker.Hubs
{
    public interface IAgentClient
    {
        Task DispatchJob(string jobId, JobRequest request);
    }
}
