using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DirectoryAnalyzer.Contracts;

namespace DirectoryAnalyzer.Services
{
    public sealed class BrokerAgentRegistryService
    {
        private readonly BrokerClientSettings _settings;
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public BrokerAgentRegistryService(BrokerClientSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public async Task<IReadOnlyList<AgentDescriptor>> GetAgentsAsync(CancellationToken token)
        {
            using var client = CreateClient();
            using var response = await client.GetAsync("/api/agents", token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<AgentDescriptor>();
            }

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<AgentDescriptor>>(body, _jsonOptions) ?? new List<AgentDescriptor>();
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

            return new HttpClient(handler)
            {
                BaseAddress = new Uri(_settings.BrokerBaseUrl),
                Timeout = TimeSpan.FromSeconds(Math.Max(1, _settings.RequestTimeoutSeconds))
            };
        }

        private static X509Certificate2 FindCertificate(string thumbprint)
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
