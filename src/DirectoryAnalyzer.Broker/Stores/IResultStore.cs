using DirectoryAnalyzer.Contracts;

namespace DirectoryAnalyzer.Broker.Stores
{
    public interface IResultStore
    {
        void StoreResult(string jobId, ModuleResult result);
        ModuleResult GetResult(string jobId);
    }
}
