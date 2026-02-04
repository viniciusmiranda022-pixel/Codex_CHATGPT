using System;
using System.Threading;
using System.Threading.Tasks;

namespace DirectoryAnalyzer.Modules
{
    public interface ICollector<T>
    {
        Task<T> CollectAsync(CancellationToken cancellationToken, IProgress<string> progress);
    }
}
