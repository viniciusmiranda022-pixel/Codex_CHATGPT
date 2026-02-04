using System;
using System.IO;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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

            _listener.Prefixes.Clear();
            _listener.Prefixes.Add(_config.BindPrefix);
            _listener.Start();

            while (!token.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleRequestAsync(context, token), token);
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

            var clientThumbprint = string.Empty;
            var requestId = string.Empty;
            var actionName = string.Empty;
            var durationMs = 0L;
            var errorCode = string.Empty;

            try
            {
                if (!await ValidateClientCertificateAsync(context, token).ConfigureAwait(false))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                    await WriteResponseAsync(context, AgentResponse.Failed("", "ClientCertificate", "Client certificate not allowed."))
                        .ConfigureAwait(false);
                    return;
                }

                var request = await ReadRequestAsync(context.Request.InputStream).ConfigureAwait(false);
                if (request == null)
                {
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

        private async Task<bool> ValidateClientCertificateAsync(HttpListenerContext context, CancellationToken token)
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
