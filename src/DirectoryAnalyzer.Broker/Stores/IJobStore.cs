using System;
using System.Collections.Generic;
using DirectoryAnalyzer.Contracts;

namespace DirectoryAnalyzer.Broker.Stores
{
    public interface IJobStore
    {
        string CreateJob(JobRequest request);

        /// <summary>Cria o job já vinculado à única conexão autorizada a atualizá-lo.</summary>
        string CreateJob(JobRequest request, string assignedConnectionId);

        /// <summary>Conexão SignalR designada no despacho; null para jobs sem vínculo.</summary>
        string GetAssignedConnectionId(string jobId);

        /// <summary>Cópia do estado atual; alterá-la não altera o job.</summary>
        JobStatus GetStatus(string jobId);
        bool TryUpdateStatus(string jobId, JobStatus status);
        JobRequest GetRequest(string jobId);
        IReadOnlyCollection<string> ListJobIds();

        /// <summary>Jobs ainda não encerrados vinculados a esta conexão.</summary>
        IReadOnlyCollection<string> ListOpenJobsForConnection(string connectionId);

        /// <summary>Registra progresso apenas se o job não estiver encerrado.</summary>
        bool TryReportProgress(string jobId, int progressPercent, string message);

        /// <summary>
        /// Encerra o job uma única vez. <paramref name="beforeCommit"/> roda sob o mesmo bloqueio, antes de o
        /// estado terminal ficar visível (ex.: gravar o resultado). Devolve false se o job já estava encerrado.
        /// </summary>
        bool TryFinish(string jobId, JobState terminalState, string message, Action beforeCommit = null);
    }
}
