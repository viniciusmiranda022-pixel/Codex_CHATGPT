using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization.Json;
 codex/design-production-grade-on-premises-agent-architecture-mn24bx
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using DirectoryAnalyzer.AgentContracts;

using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
 main

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
 codex/design-production-grade-on-premises-agent-architecture-mn24bx
            var configPath = ResolveConfigPath("agentclientsettings.json");

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var configPath = Path.Combine(baseDir, "analyzersettings.json");
 main
            var config = AnalyzerConfigLoader.Load(configPath);

            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;

            var clientCert = FindCertificate(config.ClientCertThumbprint);
            if (clientCert == null)
            {
                Console.Error.WriteLine("Client certificate not found. Ensure it is installed in CurrentUser\\My.");
                return 1;
            }

            using var handler = new HttpClientHandler();
            handler.ClientCertificates.Add(clientCert);
 codex/design-production-grade-on-premises-agent-architecture-mn24bx
            handler.CheckCertificateRevocationList = true;
            handler.SslProtocols = SslProtocols.Tls12;
            handler.ServerCertificateCustomValidationCallback = (_, cert, _, errors) =>
                cert != null &&
                errors == System.Net.Security.SslPolicyErrors.None &&
                config.AllowedAgentThumbprints.Any(tp =>

            handler.ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
                cert != null && config.AllowedAgentThumbprints.Any(tp =>
 main
                    string.Equals(tp, cert.GetCertHashString(), StringComparison.OrdinalIgnoreCase));

            using var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(config.RequestTimeoutSeconds)
            };

            var request = new AgentRequest
            {
                ActionName = "GetUsers",
                Parameters = { ["IncludeDisabled"] = "false" }
            };

            var response = await PostAsync(httpClient, new Uri(config.AgentEndpoint), request).ConfigureAwait(false);

            if (response.Error != null)
            {
                Console.Error.WriteLine($"Error: {response.Error.Code} - {response.Error.Message}");
                return 2;
            }

            Console.WriteLine($"Request {response.RequestId} completed in {response.DurationMs} ms.");
 codex/design-production-grade-on-premises-agent-architecture-mn24bx
            var payload = response.Payload as GetUsersResult;
            foreach (var user in payload?.Users ?? Array.Empty<UserRecord>())

            foreach (var user in response.Payload?.Users ?? Array.Empty<UserRecord>())
 main
            {
                Console.WriteLine($"{user.SamAccountName} | {user.DisplayName} | Enabled={user.Enabled}");
            }

            return 0;
        }

 codex/design-production-grade-on-premises-agent-architecture-mn24bx
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


 main
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
    }
}
