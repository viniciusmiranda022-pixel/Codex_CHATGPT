using System;
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

        public BrokerJobService(BrokerClientSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public async Task<ModuleResult> RunJobAsync(JobRequest request, CancellationToken token)
        {
            using var client = CreateClient();
            var jobStatus = await CreateJobAsync(client, request, token).ConfigureAwait(false);
            if (jobStatus == null)
            {
                return null;
            }

            var status = await PollStatusAsync(client, jobStatus.JobId, token).ConfigureAwait(false);
            if (status == null)
            {
                return null;
            }

            return await GetResultAsync(client, status.JobId, token).ConfigureAwait(false);
        }

        public Task<ModuleResult> RunPowerShellScriptAsync(string moduleName, string scriptText, IDictionary<string, string> parameters, string requestedBy, CancellationToken token)
        {
            var request = new JobRequest
            {
                CorrelationId = Guid.NewGuid().ToString("N"),
                ModuleName = "RunPowerShellScript",
                RequestedBy = requestedBy,
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };

            request.Parameters["Script"] = scriptText ?? string.Empty;
            request.Parameters["ModuleName"] = moduleName ?? string.Empty;

            if (parameters != null)
            {
                foreach (var pair in parameters)
                {
                    request.Parameters[pair.Key] = pair.Value;
                }
            }

            return RunJobAsync(request, token);
        }

        private async Task<JobStatus> CreateJobAsync(HttpClient client, JobRequest request, CancellationToken token)
        {
            var payload = JsonSerializer.Serialize(request, _jsonOptions);
            using var response = await client.PostAsync("/api/jobs", new StringContent(payload, Encoding.UTF8, "application/json"), token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonSerializer.Deserialize<JobStatus>(body, _jsonOptions);
        }

        private async Task<JobStatus> PollStatusAsync(HttpClient client, string jobId, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
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

            return null;
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
