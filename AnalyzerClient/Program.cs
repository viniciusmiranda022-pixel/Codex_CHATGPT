using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization.Json;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using DirectoryAnalyzer.AgentContracts;

namespace DirectoryAnalyzer.AnalyzerClient
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                return RunAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 99;
            }
        }

        private static async Task<int> RunAsync()
        {
            var configPath = ResolveConfigPath("agentclientsettings.json");
            var config = AnalyzerConfigLoader.Load(configPath);

            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;

            var clientCert = FindCertificate(config.ClientCertThumbprint);
            if (clientCert == null)
            {
                Console.Error.WriteLine("Client certificate not found. Ensure it is installed in CurrentUser\\My.");
                return 1;
            }

            using var handler = new HttpClientHandler
            {
                CheckCertificateRevocationList = true,
                SslProtocols = SslProtocols.Tls12
            };
            handler.ClientCertificates.Add(clientCert);
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                ValidateServerCertificate(message?.RequestUri, cert, chain, errors, config);

            using var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(config.RequestTimeoutSeconds)
            };

            var request = new AgentRequest
            {
                ActionName = "GetUsers",
                TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Nonce = CreateNonce(),
                CorrelationId = Guid.NewGuid().ToString("N"),
                Parameters = { ["IncludeDisabled"] = "false" }
            };
            request.Signature = AgentRequestSigner.Sign(request, clientCert);

            var response = await PostAsync(httpClient, new Uri(config.AgentEndpoint), request).ConfigureAwait(false);

            if (response.Error != null)
            {
                Console.Error.WriteLine($"Error: {response.Error.Code} - {response.Error.Message}");
                return 2;
            }

            Console.WriteLine($"Request {response.RequestId} completed in {response.DurationMs} ms.");

            var payload = response.Payload as GetUsersResult;
            foreach (var user in payload?.Users ?? Array.Empty<UserRecord>())
            {
                Console.WriteLine($"{user.SamAccountName} | {user.DisplayName} | Enabled={user.Enabled}");
            }

            return 0;
        }

        private static string ResolveConfigPath(string fileName)
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var sharedPath = Path.Combine(programData, "DirectoryAnalyzer", fileName);
            if (File.Exists(sharedPath))
            {
                return sharedPath;
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
        }

        private static X509Certificate2 FindCertificate(string thumbprint)
        {
            if (string.IsNullOrWhiteSpace(thumbprint))
            {
                return null;
            }

            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            var matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
            return matches.Count > 0 ? matches[0] : null;
        }

        private static string CreateNonce()
        {
            var bytes = new byte[16];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        private static async Task<AgentResponse> PostAsync(HttpClient client, Uri endpoint, AgentRequest request)
        {
            var serializer = new DataContractJsonSerializer(typeof(AgentRequest));
            using var buffer = new MemoryStream();
            serializer.WriteObject(buffer, request);

            var content = new ByteArrayContent(buffer.ToArray());
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            using var response = await client.PostAsync(endpoint, content).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var responseSerializer = new DataContractJsonSerializer(typeof(AgentResponse));
            return (AgentResponse)responseSerializer.ReadObject(responseStream);
        }

        private static bool ValidateServerCertificate(
            Uri endpoint,
            X509Certificate certificate,
            X509Chain chain,
            System.Net.Security.SslPolicyErrors errors,
            AnalyzerConfig config)
        {
            if (certificate == null || errors.HasFlag(System.Net.Security.SslPolicyErrors.RemoteCertificateNotAvailable))
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

            if (!ValidateCertificateChain(cert2, config.EnforceRevocationCheck, config.FailOpenOnRevocation))
            {
                return false;
            }

            var allowedThumbprints = config.AllowedAgentThumbprints ?? Array.Empty<string>();
            if (allowedThumbprints.Length > 0)
            {
                var thumbprint = cert2.GetCertHashString();
                return allowedThumbprints.Any(tp => string.Equals(tp, thumbprint, StringComparison.OrdinalIgnoreCase));
            }

            return true;
        }

        private static bool ValidateCertificateChain(X509Certificate2 cert, bool enforceRevocation, bool failOpenOnRevocation)
        {
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
                Console.Error.WriteLine("Warning: server certificate revocation status could not be verified; fail-open enabled.");
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
    }
}
