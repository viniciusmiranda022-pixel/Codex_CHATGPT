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
using System.Threading;
using System.Threading.Tasks;
using DirectoryAnalyzer.Agent.Contracts;

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
                ValidateServerCertificate(message?.RequestUri, cert, chain, errors, settings);

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds)
            };

            var request = new AgentRequest
            {
                ActionName = "GetUsers",
                TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Nonce = Guid.NewGuid().ToString("N"),
                CorrelationId = Guid.NewGuid().ToString("N"),
                Parameters = { ["IncludeDisabled"] = includeDisabled.ToString() }
            };

            request.Signature = AgentRequestSigner.Sign(request, clientCert);
            var response = await PostAsync(client, new Uri(settings.AgentEndpoint), request, token).ConfigureAwait(false);

            if (response.Error != null)
            {
                throw new InvalidOperationException($"Agent error: {response.Error.Code} - {response.Error.Message}");
            }

            return response.Payload as GetUsersResult ?? new GetUsersResult();
        }

        private bool ValidateServerCertificate(
            Uri endpoint,
            X509Certificate cert,
            X509Chain chain,
            SslPolicyErrors errors,
            AgentClientSettings settings)
        {
            if (cert == null || errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
            {
                return false;
            }

            if (endpoint == null)
            {
                return false;
            }

            var cert2 = cert as X509Certificate2 ?? new X509Certificate2(cert);
            if (!ValidateServerHostname(endpoint.Host, cert2))
            {
                return false;
            }

            if (!ValidateCertificateChain(cert2, settings.EnforceRevocationCheck, settings.FailOpenOnRevocation, out var warning))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(warning))
            {
                _logService.Warn(warning);
            }

            var allowedThumbprints = settings.AllowedAgentThumbprints ?? Array.Empty<string>();
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

        private async Task<AgentResponse> PostAsync(HttpClient client, Uri endpoint, AgentRequest request, CancellationToken token)
        {
            var serializer = new DataContractJsonSerializer(typeof(AgentRequest));
            using var buffer = new MemoryStream();
            serializer.WriteObject(buffer, request);

            var content = new ByteArrayContent(buffer.ToArray());
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            _logService.Info($"Calling agent at {endpoint} with request {request.RequestId}.");
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = content
            };
            if (!string.IsNullOrWhiteSpace(request.CorrelationId))
            {
                httpRequest.Headers.Add("X-Correlation-Id", request.CorrelationId);
            }
            using var response = await client.SendAsync(httpRequest, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var responseSerializer = new DataContractJsonSerializer(typeof(AgentResponse));
            return (AgentResponse)responseSerializer.ReadObject(responseStream);
        }
    }
}
