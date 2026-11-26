using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DirectoryAnalyzer.Contracts;

namespace DirectoryAnalyzer.Services
{
    public sealed class BrokerJobService
    {
        private readonly BrokerClientSettings _settings;
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private readonly Func<HttpMessageHandler> _handlerFactory;

        public BrokerJobService(BrokerClientSettings settings)
            : this(settings, null)
        {
        }

        /// <summary>Permite injetar o transporte HTTP (testes de integração com Broker em memória).</summary>
        public BrokerJobService(BrokerClientSettings settings, Func<HttpMessageHandler> handlerFactory)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _handlerFactory = handlerFactory;
        }

        /// <summary>
        /// Envia o job ao agente alvo e classifica o desfecho. Nunca devolve null: recusa do Broker,
        /// recusa do agente, agente indisponível e coleta vazia chegam como estados distintos.
        /// </summary>
        public async Task<CollectionOutcome> ExecuteAsync(JobRequest request, CancellationToken token)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if ((request.TargetAgentIds == null || request.TargetAgentIds.Count == 0)
                && !string.IsNullOrWhiteSpace(_settings.TargetAgentId))
            {
                request.TargetAgentIds = new List<string> { _settings.TargetAgentId.Trim() };
            }

            if (request.TargetAgentIds == null || request.TargetAgentIds.Count == 0)
            {
                return CollectionOutcome.Rejected("NoTargetConfigured",
                    "Nenhum agente alvo configurado (Agentes → Agente alvo). Nada foi enviado.");
            }

            try
            {
                using var client = CreateClient();
                var created = await CreateJobAsync(client, request, token).ConfigureAwait(false);
                if (created.Outcome != null)
                {
                    return created.Outcome;
                }

                var status = await PollStatusAsync(client, created.Status.JobId, token).ConfigureAwait(false);
                if (status == null)
                {
                    return CollectionOutcome.Failed("JobLost", "O Broker não reconhece mais o job.");
                }

                var result = await GetResultAsync(client, status.JobId, token).ConfigureAwait(false);
                if (result == null && status.State == JobState.Failed)
                {
                    // O Broker encerrou o job sem resultado: a conexão do agente caiu antes da entrega.
                    return CollectionOutcome.AgentUnavailable("AgentDisconnected", status.Message);
                }

                return CollectionOutcome.FromModuleResult(result);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return CollectionOutcome.Canceled();
            }
            catch (OperationCanceledException)
            {
                return CollectionOutcome.AgentUnavailable("BrokerTimeout", "O Broker não respondeu dentro do tempo limite.");
            }
            catch (HttpRequestException ex)
            {
                return CollectionOutcome.AgentUnavailable("BrokerUnreachable", "Não foi possível falar com o Broker: " + ex.Message);
            }
        }

        /// <summary>
        /// Compatibilidade: devolve o ModuleResult somente quando o agente executou o módulo.
        /// Nos demais casos lança <see cref="CollectionNotExecutedException"/> em vez de devolver null,
        /// para que nenhuma tela exiba "0 itens" após uma recusa.
        /// </summary>
        public async Task<ModuleResult> RunJobAsync(JobRequest request, CancellationToken token)
        {
            var outcome = await ExecuteAsync(request, token).ConfigureAwait(false);
            if (outcome.Kind == CollectionOutcomeKind.Canceled)
            {
                throw new OperationCanceledException(token);
            }

            if (!outcome.IsSuccess)
            {
                throw new CollectionNotExecutedException(outcome);
            }

            return outcome.Result;
        }

        public Task<CollectionOutcome> ExecuteModuleAsync(string moduleName, IDictionary<string, string> parameters, string requestedBy, CancellationToken token)
        {
            return ExecuteAsync(CreateModuleRequest(moduleName, parameters, requestedBy), token);
        }

        public Task<ModuleResult> RunModuleAsync(string moduleName, IDictionary<string, string> parameters, string requestedBy, CancellationToken token)
        {
            return RunJobAsync(CreateModuleRequest(moduleName, parameters, requestedBy), token);
        }

        /// <summary>
        /// Mantido para as telas ainda não migradas. Não envia nada: o agente não executa scripts do
        /// solicitante, então a recusa é imediata e local.
        /// </summary>
        public Task<ModuleResult> RunPowerShellScriptAsync(string moduleName, string scriptText, IDictionary<string, string> parameters, string requestedBy, CancellationToken token)
        {
            if (!ModuleAvailability.TryGetUnavailable(moduleName, out var outcome))
            {
                outcome = CollectionOutcome.ModuleUnavailable(ModuleAvailability.PendingMigrationCode,
                    "O agente não executa scripts enviados pela WPF; solicite um módulo do catálogo pelo nome.");
            }

            throw new CollectionNotExecutedException(outcome);
        }

        private static JobRequest CreateModuleRequest(string moduleName, IDictionary<string, string> parameters, string requestedBy)
        {
            return new JobRequest
            {
                CorrelationId = Guid.NewGuid().ToString("N"),
                ModuleName = moduleName,
                RequestedBy = requestedBy,
                Parameters = parameters == null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase)
            };
        }

        private sealed class CreateJobResponse
        {
            public JobStatus Status { get; set; }
            public CollectionOutcome Outcome { get; set; }
        }

        private sealed class BrokerErrorBody
        {
            public string Code { get; set; }
            public string Message { get; set; }
        }

        private async Task<CreateJobResponse> CreateJobAsync(HttpClient client, JobRequest request, CancellationToken token)
        {
            var payload = JsonSerializer.Serialize(request, _jsonOptions);
            using var response = await client.PostAsync("/api/jobs", new StringContent(payload, Encoding.UTF8, "application/json"), token)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return new CreateJobResponse { Status = JsonSerializer.Deserialize<JobStatus>(body, _jsonOptions) };
            }

            var error = TryParseError(body);
            var code = string.IsNullOrWhiteSpace(error?.Code) ? $"Http{(int)response.StatusCode}" : error.Code;
            var message = string.IsNullOrWhiteSpace(error?.Message) ? "O Broker recusou o pedido." : error.Message;

            CollectionOutcome outcome;
            if ((int)response.StatusCode == 409 && string.Equals(code, "AgentUnavailable", StringComparison.Ordinal))
            {
                outcome = CollectionOutcome.AgentUnavailable(code, message);
            }
            else if ((int)response.StatusCode >= 500)
            {
                outcome = CollectionOutcome.AgentUnavailable(code, "Erro no Broker: " + message);
            }
            else
            {
                outcome = CollectionOutcome.Rejected(code, message);
            }

            return new CreateJobResponse { Outcome = outcome };
        }

        private BrokerErrorBody TryParseError(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<BrokerErrorBody>(body, _jsonOptions);
            }
            catch (JsonException)
            {
                return new BrokerErrorBody { Message = body.Length > 200 ? body.Substring(0, 200) : body };
            }
        }

        private async Task<JobStatus> PollStatusAsync(HttpClient client, string jobId, CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                using var response = await client.GetAsync($"/api/jobs/{jobId}", token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var status = JsonSerializer.Deserialize<JobStatus>(body, _jsonOptions);
                if (status == null)
                {
                    return null;
                }

                if (status.State == JobState.Completed || status.State == JobState.Failed)
                {
                    return status;
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _settings.PollIntervalSeconds)), token).ConfigureAwait(false);
            }
        }

        private async Task<ModuleResult> GetResultAsync(HttpClient client, string jobId, CancellationToken token)
        {
            using var response = await client.GetAsync($"/api/jobs/{jobId}/result", token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonSerializer.Deserialize<ModuleResult>(body, _jsonOptions);
        }

        private HttpClient CreateClient()
        {
            if (_handlerFactory != null)
            {
                return new HttpClient(_handlerFactory(), disposeHandler: true)
                {
                    BaseAddress = new Uri(_settings.BrokerBaseUrl),
                    Timeout = TimeSpan.FromSeconds(Math.Max(1, _settings.RequestTimeoutSeconds))
                };
            }

            var handler = new HttpClientHandler
            {
                SslProtocols = SslProtocols.Tls12
            };

            var cert = FindCertificate(_settings.ClientCertThumbprint);
            if (cert != null)
            {
                handler.ClientCertificates.Add(cert);
            }

            var client = new HttpClient(handler)
            {
                BaseAddress = new Uri(_settings.BrokerBaseUrl),
                Timeout = TimeSpan.FromSeconds(Math.Max(1, _settings.RequestTimeoutSeconds))
            };

            return client;
        }

        private X509Certificate2 FindCertificate(string thumbprint)
        {
            if (string.IsNullOrWhiteSpace(thumbprint))
            {
                return null;
            }

            var normalized = thumbprint.Replace(" ", string.Empty);
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            var matches = store.Certificates.Find(X509FindType.FindByThumbprint, normalized, false);
            return matches.Count > 0 ? matches[0] : null;
        }
    }
}
