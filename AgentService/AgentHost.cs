using System;
using System.IO;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography.X509Certificates;
 codex/design-production-grade-on-premises-agent-architecture-mn24bx
using System.Threading;
using System.Threading.Tasks;
using DirectoryAnalyzer.AgentContracts;

using System.Text;
using System.Threading;
using System.Threading.Tasks;
 main

namespace DirectoryAnalyzer.Agent
{
    public sealed class AgentHost
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly string _configPath;
        private AgentConfig _config;
        private ActionRegistry _registry;
        private AgentLogger _logger;

        public AgentHost(string configPath)
        {
            _configPath = configPath;
        }

        public async Task StartAsync(CancellationToken token)
        {
            _config = ConfigLoader.Load(_configPath);
            _registry = new ActionRegistry();
            _logger = new AgentLogger(_config.LogPath);

 codex/design-production-grade-on-premises-agent-architecture-mn24bx
            EnsureSecurePrefix(_config.BindPrefix);
            EnsureServerCertificateAvailable(_config.CertThumbprint);
            EnsureClientAllowList(_config.AnalyzerClientThumbprints);


 main
            _listener.Prefixes.Clear();
            _listener.Prefixes.Add(_config.BindPrefix);
            _listener.Start();

            while (!token.IsCancellationRequested)
            {
 codex/design-production-grade-on-premises-agent-architecture-mn24bx
                try
                {
                    var context = await _listener.GetContextAsync().ConfigureAwait(false);
                    _ = Task.Run(() => HandleRequestAsync(context, token), token);
                }
                catch (HttpListenerException)
                {
                    break;
                }

                var context = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleRequestAsync(context, token), token);
 main
            }
        }

        public Task StopAsync()
        {
            if (_listener.IsListening)
            {
                _listener.Stop();
            }

            return Task.CompletedTask;
        }

        private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken token)
        {
            context.Response.ContentType = "application/json";
            context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
 codex/design-production-grade-on-premises-agent-architecture-mn24bx
            context.Response.Headers.Add("Cache-Control", "no-store");

 main

            var clientThumbprint = string.Empty;
            var requestId = string.Empty;
            var actionName = string.Empty;
            var durationMs = 0L;
            var errorCode = string.Empty;

            try
            {
 codex/design-production-grade-on-premises-agent-architecture-mn24bx
                if (!context.Request.IsSecureConnection)
                {
                    errorCode = "TransportSecurity";
                    context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                    await WriteResponseAsync(context, AgentResponse.Failed("", "TransportSecurity", "HTTPS is required."))
                        .ConfigureAwait(false);
                    return;
                }

                if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    errorCode = "MethodNotAllowed";
                    context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                    await WriteResponseAsync(context, AgentResponse.Failed("", "MethodNotAllowed", "Only POST is supported."))
                        .ConfigureAwait(false);
                    return;
                }

                if (context.Request.ContentLength64 > 0 && context.Request.ContentLength64 > _config.MaxRequestBytes)
                {
                    errorCode = "RequestTooLarge";
                    context.Response.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
                    await WriteResponseAsync(context, AgentResponse.Failed("", "RequestTooLarge", "Request exceeds size limit."))
                        .ConfigureAwait(false);
                    return;
                }

                if (context.Request.ContentType == null || !context.Request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                {
                    errorCode = "InvalidContentType";
                    context.Response.StatusCode = (int)HttpStatusCode.UnsupportedMediaType;
                    await WriteResponseAsync(context, AgentResponse.Failed("", "InvalidContentType", "Content-Type must be application/json."))
                        .ConfigureAwait(false);
                    return;
                }

                if (!await ValidateClientCertificateAsync(context).ConfigureAwait(false))
                {
                    errorCode = "ClientCertificate";

                if (!await ValidateClientCertificateAsync(context, token).ConfigureAwait(false))
                {
 main
                    context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                    await WriteResponseAsync(context, AgentResponse.Failed("", "ClientCertificate", "Client certificate not allowed."))
                        .ConfigureAwait(false);
                    return;
                }

                var request = await ReadRequestAsync(context.Request.InputStream).ConfigureAwait(false);
                if (request == null)
                {
 codex/design-production-grade-on-premises-agent-architecture-mn24bx
                    errorCode = "InvalidRequest";

 main
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    await WriteResponseAsync(context, AgentResponse.Failed("", "InvalidRequest", "Request body invalid."))
                        .ConfigureAwait(false);
                    return;
                }

                requestId = request.RequestId;
                actionName = request.ActionName;

                var response = await _registry.ExecuteAsync(request, _config, token).ConfigureAwait(false);
                durationMs = response.DurationMs;
                errorCode = response.Error?.Code;

                context.Response.StatusCode = (int)HttpStatusCode.OK;
                await WriteResponseAsync(context, response).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                errorCode = "UnhandledException";
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await WriteResponseAsync(context, AgentResponse.FromException(requestId, ex)).ConfigureAwait(false);
            }
            finally
            {
                clientThumbprint = context.Request.GetClientCertificate()?.GetCertHashString() ?? string.Empty;
                var status = string.IsNullOrWhiteSpace(errorCode) ? "Success" : "Failed";
                _logger.Write(AgentLogger.Create(requestId, actionName, clientThumbprint, durationMs, status, errorCode));
                context.Response.OutputStream.Close();
            }
        }

 codex/design-production-grade-on-premises-agent-architecture-mn24bx
        private async Task<bool> ValidateClientCertificateAsync(HttpListenerContext context)

        private async Task<bool> ValidateClientCertificateAsync(HttpListenerContext context, CancellationToken token)
 main
        {
            var cert = await context.Request.GetClientCertificateAsync().ConfigureAwait(false);
            if (cert == null)
            {
                return false;
            }

            var thumbprint = cert.GetCertHashString();
            foreach (var allowed in _config.AnalyzerClientThumbprints)
            {
                if (string.Equals(allowed, thumbprint, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

 codex/design-production-grade-on-premises-agent-architecture-mn24bx
        private static void EnsureSecurePrefix(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix) || !prefix.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("BindPrefix must start with https://");
            }
        }

        private static void EnsureServerCertificateAvailable(string thumbprint)
        {
            if (string.IsNullOrWhiteSpace(thumbprint))
            {
                throw new InvalidOperationException("CertThumbprint must be configured.");
            }

            using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);
            var matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
            if (matches.Count == 0)
            {
                throw new InvalidOperationException("Agent TLS certificate not found in LocalMachine\\\\My.");
            }
        }

        private static void EnsureClientAllowList(string[] thumbprints)
        {
            if (thumbprints == null || thumbprints.Length == 0)
            {
                throw new InvalidOperationException("AnalyzerClientThumbprints must include at least one thumbprint.");
            }
        }


 main
        private static Task WriteResponseAsync(HttpListenerContext context, AgentResponse response)
        {
            var serializer = new DataContractJsonSerializer(typeof(AgentResponse));
            using var buffer = new MemoryStream();
            serializer.WriteObject(buffer, response);
            var payload = buffer.ToArray();
            return context.Response.OutputStream.WriteAsync(payload, 0, payload.Length);
        }

        private static async Task<AgentRequest> ReadRequestAsync(Stream inputStream)
        {
            if (inputStream == null || !inputStream.CanRead)
            {
                return null;
            }

            using var mem = new MemoryStream();
            await inputStream.CopyToAsync(mem).ConfigureAwait(false);
            mem.Position = 0;

            var serializer = new DataContractJsonSerializer(typeof(AgentRequest));
            return serializer.ReadObject(mem) as AgentRequest;
        }
    }
}
