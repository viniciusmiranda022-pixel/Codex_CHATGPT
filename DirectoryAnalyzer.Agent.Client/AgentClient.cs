using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Runtime.Serialization.Json;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using DirectoryAnalyzer.Agent.Contracts;

namespace DirectoryAnalyzer.Agent.Client
{
    public sealed class AgentClientOptions
    {
        public Uri Endpoint { get; set; }
        public string ClientCertThumbprint { get; set; } = string.Empty;
        public StoreLocation ClientCertStoreLocation { get; set; } = StoreLocation.CurrentUser;
        public string[] AllowedServerThumbprints { get; set; } = Array.Empty<string>();
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
        public int MaxRetries { get; set; } = 3;
        public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromMilliseconds(250);
    }

    public sealed class AgentClientException : Exception
    {
        public AgentClientException(string message, string errorCode, HttpStatusCode? statusCode, Exception innerException = null)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
            StatusCode = statusCode;
        }

        public string ErrorCode { get; }
        public HttpStatusCode? StatusCode { get; }
    }

    public interface IAgentClient
    {
        Task<GetUsersResult> GetUsersAsync(bool includeDisabled, string correlationId, CancellationToken token);
    }

    public sealed class AgentClient : IAgentClient
    {
        private readonly AgentClientOptions _options;

        public AgentClient(AgentClientOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (_options.Endpoint == null)
            {
                throw new ArgumentException("Endpoint is required.", nameof(options));
            }
        }

        public async Task<GetUsersResult> GetUsersAsync(bool includeDisabled, string correlationId, CancellationToken token)
        {
            var request = BuildRequest("GetUsers", correlationId);
            request.Parameters["IncludeDisabled"] = includeDisabled.ToString();

            var response = await SendAsync(request, token).ConfigureAwait(false);
            if (response.Error != null)
            {
                throw new AgentClientException(
                    $"Agent error: {response.Error.Code} - {response.Error.Message}",
                    response.Error.Code,
                    null);
            }

            return response.Payload as GetUsersResult ?? new GetUsersResult();
        }

        private AgentRequest BuildRequest(string actionName, string correlationId)
        {
            return new AgentRequest
            {
                ActionName = actionName,
                TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Nonce = Guid.NewGuid().ToString("N"),
                CorrelationId = correlationId
            };
        }

        private async Task<AgentResponse> SendAsync(AgentRequest request, CancellationToken token)
        {
            var cert = FindCertificate(_options.ClientCertThumbprint, _options.ClientCertStoreLocation);
            if (cert == null)
            {
                throw new AgentClientException("Client certificate not found.", "ClientCertificate", null);
            }

            using var handler = new HttpClientHandler
            {
                CheckCertificateRevocationList = true,
                SslProtocols = SslProtocols.Tls12
            };

            handler.ClientCertificates.Add(cert);
            handler.ServerCertificateCustomValidationCallback = (message, certificate, chain, errors) =>
                ValidateServerCertificate(certificate, errors, _options.AllowedServerThumbprints);

            using var client = new HttpClient(handler)
            {
                Timeout = _options.Timeout
            };

            var attempt = 0;
            Exception lastError = null;
            while (attempt <= _options.MaxRetries)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    return await PostAsync(client, _options.Endpoint, request, cert, token).ConfigureAwait(false);
                }
                catch (AgentClientException)
                {
                    throw;
                }
                catch (Exception ex) when (IsTransient(ex) && attempt < _options.MaxRetries)
                {
                    lastError = ex;
                    var delay = GetDelay(attempt);
                    await Task.Delay(delay, token).ConfigureAwait(false);
                    attempt++;
                }
            }

            throw new AgentClientException("Agent request failed after retries.", "RetryExceeded", null, lastError);
        }

        private async Task<AgentResponse> PostAsync(HttpClient client, Uri endpoint, AgentRequest request, X509Certificate2 cert, CancellationToken token)
        {
            request.Signature = AgentRequestSigner.Sign(request, cert);

            var serializer = new DataContractJsonSerializer(typeof(AgentRequest));
            using var buffer = new MemoryStream();
            serializer.WriteObject(buffer, request);

            var content = new ByteArrayContent(buffer.ToArray());
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = content
            };

            if (!string.IsNullOrWhiteSpace(request.CorrelationId))
            {
                httpRequest.Headers.Add("X-Correlation-Id", request.CorrelationId);
            }

            using var response = await client.SendAsync(httpRequest, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new AgentClientException(
                    $"Agent responded with {(int)response.StatusCode}",
                    "HttpError",
                    response.StatusCode);
            }

            using var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var responseSerializer = new DataContractJsonSerializer(typeof(AgentResponse));
            return (AgentResponse)responseSerializer.ReadObject(responseStream);
        }

        private static bool ValidateServerCertificate(X509Certificate certificate, SslPolicyErrors errors, string[] allowedThumbprints)
        {
            if (certificate == null || errors != SslPolicyErrors.None)
            {
                return false;
            }

            var thumbprint = certificate.GetCertHashString();
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

        private static bool IsTransient(Exception ex)
        {
            if (ex is TaskCanceledException)
            {
                return true;
            }

            if (ex is HttpRequestException)
            {
                return true;
            }

            return false;
        }

        private TimeSpan GetDelay(int attempt)
        {
            var multiplier = Math.Pow(2, attempt);
            var delayMs = _options.BaseRetryDelay.TotalMilliseconds * multiplier;
            var jitter = new Random().Next(50, 200);
            return TimeSpan.FromMilliseconds(delayMs + jitter);
        }
    }
}
