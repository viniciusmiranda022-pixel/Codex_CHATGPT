using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Runtime.Serialization.Json;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using DirectoryAnalyzer.AgentContracts;

namespace DirectoryAnalyzer.Services
{
    public sealed class AgentClientService
    {
        private readonly string _settingsPath;
        private readonly ILogService _logService;

        public AgentClientService(string settingsPath, ILogService logService)
        {
            _settingsPath = settingsPath;
            _logService = logService;
        }

        public async Task<GetUsersResult> GetUsersAsync(bool includeDisabled, CancellationToken token)
        {
            var settings = AgentClientSettingsLoader.Load(_settingsPath);
            var clientCert = FindCertificate(settings.ClientCertThumbprint, StoreLocation.CurrentUser);

            if (clientCert == null)
            {
                throw new InvalidOperationException("Client certificate not found in CurrentUser\\My.");
            }

            using var handler = new HttpClientHandler
            {
                CheckCertificateRevocationList = true,
                SslProtocols = SslProtocols.Tls12
            };

            handler.ClientCertificates.Add(clientCert);
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                ValidateServerCertificate(cert, errors, settings.AllowedAgentThumbprints);

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds)
            };

            var request = new AgentRequest
            {
                ActionName = "GetUsers",
                Parameters = { ["IncludeDisabled"] = includeDisabled.ToString() }
            };

            var response = await PostAsync(client, new Uri(settings.AgentEndpoint), request, token).ConfigureAwait(false);

            if (response.Error != null)
            {
                throw new InvalidOperationException($"Agent error: {response.Error.Code} - {response.Error.Message}");
            }

            return response.Payload as GetUsersResult ?? new GetUsersResult();
        }

        private static bool ValidateServerCertificate(X509Certificate cert, SslPolicyErrors errors, string[] allowedThumbprints)
        {
            if (cert == null || errors != SslPolicyErrors.None)
            {
                return false;
            }

            var thumbprint = cert.GetCertHashString();
            return allowedThumbprints.Any(tp => string.Equals(tp, thumbprint, StringComparison.OrdinalIgnoreCase));
        }

        private static X509Certificate2 FindCertificate(string thumbprint, StoreLocation storeLocation)
        {
            if (string.IsNullOrWhiteSpace(thumbprint))
            {
                return null;
            }

            using var store = new X509Store(StoreName.My, storeLocation);
            store.Open(OpenFlags.ReadOnly);
            var matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
            return matches.Count > 0 ? matches[0] : null;
        }

        private async Task<AgentResponse> PostAsync(HttpClient client, Uri endpoint, AgentRequest request, CancellationToken token)
        {
            var serializer = new DataContractJsonSerializer(typeof(AgentRequest));
            using var buffer = new MemoryStream();
            serializer.WriteObject(buffer, request);

            var content = new ByteArrayContent(buffer.ToArray());
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            _logService.Info($"Calling agent at {endpoint} with request {request.RequestId}.");
            using var response = await client.PostAsync(endpoint, content, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var responseSerializer = new DataContractJsonSerializer(typeof(AgentResponse));
            return (AgentResponse)responseSerializer.ReadObject(responseStream);
        }
    }
}
