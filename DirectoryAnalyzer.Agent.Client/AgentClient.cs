using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Runtime.Serialization.Json;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Diagnostics;
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
        public bool EnforceRevocationCheck { get; set; } = true;
        public bool FailOpenOnRevocation { get; set; } = false;
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
                CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId
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
                ValidateServerCertificate(message?.RequestUri, certificate, chain, errors, _options);

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

        private static bool ValidateServerCertificate(
            Uri endpoint,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors errors,
            AgentClientOptions options)
        {
            if (certificate == null || errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
            {
                return false;
            }

            if (endpoint == null)
            {
                return false;
            }

            var cert2 = certificate as X509Certificate2 ?? new X509Certificate2(certificate);
            if (!ValidateServerHostname(endpoint.Host, cert2))
            {
                return false;
            }

            if (!ValidateCertificateChain(cert2, options.EnforceRevocationCheck, options.FailOpenOnRevocation, out var warning))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(warning))
            {
                Trace.TraceWarning(warning);
            }

            var allowedThumbprints = options.AllowedServerThumbprints ?? Array.Empty<string>();
            if (allowedThumbprints.Length > 0)
            {
                var thumbprint = cert2.GetCertHashString();
                return allowedThumbprints.Any(tp => string.Equals(tp, thumbprint, StringComparison.OrdinalIgnoreCase));
            }

            return true;
        }

        private static bool ValidateCertificateChain(
            X509Certificate2 cert,
            bool enforceRevocation,
            bool failOpenOnRevocation,
            out string warning)
        {
            warning = string.Empty;
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = enforceRevocation ? X509RevocationMode.Online : X509RevocationMode.NoCheck;
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
            chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(10);
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            var valid = chain.Build(cert);
            if (valid)
            {
                return true;
            }

            if (failOpenOnRevocation && IsRevocationOnly(chain.ChainStatus))
            {
                warning = "Server certificate revocation status could not be verified; fail-open enabled.";
                return true;
            }

            return false;
        }

        private static bool IsRevocationOnly(X509ChainStatus[] statuses)
        {
            if (statuses == null || statuses.Length == 0)
            {
                return false;
            }

            foreach (var status in statuses)
            {
                if (status.Status != X509ChainStatusFlags.RevocationStatusUnknown
                    && status.Status != X509ChainStatusFlags.OfflineRevocation)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateServerHostname(string host, X509Certificate2 cert)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            var subjectAltNames = GetSubjectAltNames(cert);
            if (subjectAltNames.Count > 0)
            {
                return MatchesHost(host, subjectAltNames);
            }

            var commonName = cert.GetNameInfo(X509NameType.DnsName, false);
            return MatchesHost(host, new[] { commonName });
        }

        private static bool MatchesHost(string host, IEnumerable<string> names)
        {
            var isIp = IPAddress.TryParse(host, out _);
            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (isIp)
                {
                    if (string.Equals(name, host, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    continue;
                }

                if (name.StartsWith("*.", StringComparison.Ordinal))
                {
                    var suffix = name.Substring(1);
                    if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    continue;
                }

                if (string.Equals(name, host, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<string> GetSubjectAltNames(X509Certificate2 cert)
        {
            var names = new List<string>();
            var extension = cert.Extensions["2.5.29.17"];
            if (extension == null)
            {
                return names;
            }

            var formatted = extension.Format(true);
            var entries = formatted.Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var entry in entries)
            {
                var trimmed = entry.Trim();
                if (trimmed.StartsWith("DNS Name=", StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(trimmed.Substring("DNS Name=".Length));
                }
                else if (trimmed.StartsWith("IP Address=", StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(trimmed.Substring("IP Address=".Length));
                }
            }

            return names;
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
