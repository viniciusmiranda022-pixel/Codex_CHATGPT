using System.Collections.Concurrent;
using DirectoryAnalyzer.Contracts;

namespace DirectoryAnalyzer.Broker.Stores
{
    public sealed class InMemoryResultStore : IResultStore
    {
        private readonly ConcurrentDictionary<string, ModuleResult> _results = new ConcurrentDictionary<string, ModuleResult>();

        public void StoreResult(string jobId, ModuleResult result)
        {
            _results[jobId] = result;
        }

        public ModuleResult GetResult(string jobId)
        {
            return _results.TryGetValue(jobId, out var result) ? result : null;
        }
    }
}
