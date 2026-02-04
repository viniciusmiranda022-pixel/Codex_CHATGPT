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
using DirectoryAnalyzer.Agent.Contracts;
using DirectoryAnalyzer.Agent.Contracts.Services;

namespace DirectoryAnalyzer.AnalyzerClient
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                if (HasDoctorFlag(args))
                {
                    return RunDoctor();
                }

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
            var policy = new PathPolicy();
            var resolver = new ConfigurationResolver(policy);
            var resolution = resolver.ResolveAnalyzerClientConfig();
            var logger = new DiagnosticLogger(resolution.LogPath);
            LogResolution(logger, resolution);

            var config = AnalyzerConfigLoader.Load(resolution.SelectedPath);

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
            foreach (var user in payload?.Users ?? new List<UserRecord>())
            {
                Console.WriteLine($"{user.SamAccountName} | {user.DisplayName} | Enabled={user.Enabled}");
            }

            return 0;
        }

        private static int RunDoctor()
        {
            var policy = new PathPolicy();
            var resolver = new ConfigurationResolver(policy);
            var resolution = resolver.ResolveAnalyzerClientConfig();
            var logger = new DiagnosticLogger(resolution.LogPath);
            var failures = 0;

            WriteDiagnostic(logger, $"Modo doctor iniciado. Config path resolvido: {resolution.SelectedPath}");
            WriteDiagnostic(logger, $"Log path: {resolution.LogPath}");
            WriteDiagnostic(logger, $"Precedência: {string.Join(", ", resolution.PrecedenceOrder)}. Fonte: {resolution.Source}.");
            if (!string.IsNullOrWhiteSpace(resolution.MigrationDetails))
            {
                WriteDiagnostic(logger, resolution.MigrationDetails);
            }

            if (!ValidateLogPath(resolution.LogPath, logger))
            {
                failures++;
            }

            if (!AnalyzerConfigLoader.TryLoad(resolution.SelectedPath, out var config, out var error))
            {
                failures++;
                WriteDiagnostic(logger, $"Falha ao ler JSON de config em {resolution.SelectedPath}. Erro: {error}");
                return 1;
            }

            var validationErrors = AnalyzerConfigValidator.Validate(config);
            if (validationErrors.Count > 0)
            {
                failures++;
                WriteDiagnostic(logger, $"Campos obrigatórios ausentes ou inválidos: {string.Join("; ", validationErrors)}");
            }

            WriteDiagnostic(logger, failures == 0 ? "Doctor concluído com sucesso." : "Doctor concluiu com falhas.");
            return failures == 0 ? 0 : 1;
        }

        private static void LogResolution(DiagnosticLogger logger, ConfigurationResolutionResult resolution)
        {
            if (logger == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(resolution.MigrationDetails))
            {
                logger.Write(resolution.MigrationDetails);
            }

            logger.Write($"Config path: {resolution.SelectedPath}. Fonte: {resolution.Source}.");
        }

        private static bool HasDoctorFlag(string[] args)
        {
            if (args == null)
            {
                return false;
            }

            foreach (var arg in args)
            {
                if (string.Equals(arg, "--doctor", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void WriteDiagnostic(DiagnosticLogger logger, string message)
        {
            Console.WriteLine(message);
            logger?.Write(message);
        }

        private static bool ValidateLogPath(string logPath, DiagnosticLogger logger)
        {
            try
            {
                var directory = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var testFile = Path.Combine(directory ?? AppDomain.CurrentDomain.BaseDirectory, $"doctor_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(testFile, "ok");
                File.Delete(testFile);
                WriteDiagnostic(logger, "Validação de escrita no diretório de log concluída.");
                return true;
            }
            catch (Exception ex)
            {
                WriteDiagnostic(logger, $"Falha ao validar escrita no diretório de log. Erro: {ex.Message}");
                return false;
            }
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
