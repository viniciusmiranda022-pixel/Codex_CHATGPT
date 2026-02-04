using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using DirectoryAnalyzer.Agent.Contracts;

namespace DirectoryAnalyzer.Agent
{
    public sealed class AgentHost
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly string _configPath;
        private AgentConfig _config;
        private ActionRegistry _registry;
        private AgentLogger _logger;
        private SlidingWindowRateLimiter _rateLimiter;
        private NonceCache _nonceCache;
        private SemaphoreSlim _concurrencyLimiter;

        public AgentHost(string configPath)
        {
            _configPath = configPath;
        }

        public async Task StartAsync(CancellationToken token)
        {
            _config = ConfigLoader.Load(_configPath);
            _registry = new ActionRegistry();
            _logger = new AgentLogger(_config.LogPath);
            _rateLimiter = new SlidingWindowRateLimiter(
                _config.MaxRequestsPerMinute,
                TimeSpan.FromMinutes(1),
                _config.MaxBurstRequests,
                TimeSpan.FromSeconds(_config.BurstWindowSeconds),
                TimeSpan.FromSeconds(_config.RateLimitBackoffSeconds));
            _nonceCache = new NonceCache(TimeSpan.FromMinutes(_config.ReplayCacheMinutes));
            _concurrencyLimiter = _config.MaxConcurrentRequests > 0
                ? new SemaphoreSlim(_config.MaxConcurrentRequests, _config.MaxConcurrentRequests)
                : new SemaphoreSlim(int.MaxValue, int.MaxValue);

            EnsureSecurePrefix(_config.BindPrefix);
            EnsureServerCertificateAvailable(_config.CertThumbprint);
            EnsureClientAllowList(_config.AnalyzerClientThumbprints);

            _listener.Prefixes.Clear();
            _listener.Prefixes.Add(_config.BindPrefix);
            _listener.Start();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync().ConfigureAwait(false);
                    _ = Task.Run(() => HandleRequestAsync(context, token), token);
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
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
            context.Response.Headers.Add("Cache-Control", "no-store");

            var clientThumbprint = string.Empty;
            var clientSubject = string.Empty;
            var requestId = string.Empty;
            var actionName = string.Empty;
            var durationMs = 0L;
            var errorCode = string.Empty;
            var concurrencyLease = false;

            try
            {
                await _concurrencyLimiter.WaitAsync(token).ConfigureAwait(false);
                concurrencyLease = true;

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

                var clientCert = await ValidateClientCertificateAsync(context).ConfigureAwait(false);
                if (clientCert == null)
                {
                    errorCode = "ClientCertificate";
                    context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                    await WriteResponseAsync(context, AgentResponse.Failed("", "ClientCertificate", "Client certificate not allowed."))
                        .ConfigureAwait(false);
                    return;
                }

                clientThumbprint = clientCert.GetCertHashString();
                clientSubject = clientCert.Subject;

                var rateResult = _rateLimiter.TryAcquire(clientThumbprint);
                if (!rateResult.Allowed)
                {
                    errorCode = "RateLimited";
                    context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                    if (rateResult.RetryAfterSeconds > 0)
                    {
                        context.Response.Headers["Retry-After"] = rateResult.RetryAfterSeconds.ToString();
                    }

                    await WriteResponseAsync(
                            context,
                            AgentResponse.Failed(
                                "",
                                "RateLimited",
                                $"Request rate exceeded. Retry after {Math.Max(rateResult.RetryAfterSeconds, 1)} seconds."))
                        .ConfigureAwait(false);
                    return;
                }

                var request = await ReadRequestAsync(context.Request.InputStream).ConfigureAwait(false);
                if (request == null)
                {
                    errorCode = "InvalidRequest";
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    await WriteResponseAsync(context, AgentResponse.Failed("", "InvalidRequest", "Request body invalid."))
                        .ConfigureAwait(false);
                    return;
                }

                requestId = request.RequestId;
                actionName = request.ActionName;

                if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(actionName))
                {
                    errorCode = "InvalidRequest";
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    await WriteResponseAsync(context, AgentResponse.Failed("", "InvalidRequest", "RequestId and ActionName are required."))
                        .ConfigureAwait(false);
                    return;
                }

                if (!ValidateAntiReplay(request, clientThumbprint, out var replayError))
                {
                    errorCode = replayError;
                    context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                    await WriteResponseAsync(context, AgentResponse.Failed(requestId, replayError, "Replay protection triggered."))
                        .ConfigureAwait(false);
                    return;
                }

                if (_config.RequireSignedRequests && !AgentRequestSigner.VerifySignature(request, clientCert))
                {
                    errorCode = "InvalidSignature";
                    context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                    await WriteResponseAsync(context, AgentResponse.Failed(requestId, "InvalidSignature", "Request signature invalid."))
                        .ConfigureAwait(false);
                    return;
                }

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
                if (concurrencyLease)
                {
                    _concurrencyLimiter.Release();
                }

                var status = string.IsNullOrWhiteSpace(errorCode) ? "Success" : "Failed";
                _logger.Write(AgentLogger.Create(requestId, actionName, clientThumbprint, clientSubject, durationMs, status, errorCode));
                context.Response.OutputStream.Close();
            }
        }

        private async Task<X509Certificate2> ValidateClientCertificateAsync(HttpListenerContext context)
        {
            var cert = await context.Request.GetClientCertificateAsync().ConfigureAwait(false);
            if (cert == null)
            {
                return null;
            }

            var thumbprint = cert.GetCertHashString();
            var allowed = _config.AnalyzerClientThumbprints ?? Array.Empty<string>();
            var isAllowed = false;
            foreach (var entry in allowed)
            {
                if (string.Equals(entry, thumbprint, StringComparison.OrdinalIgnoreCase))
                {
                    isAllowed = true;
                    break;
                }
            }

            if (!isAllowed)
            {
                return null;
            }

            if (!ValidateCertificateChain(cert, _config.EnforceRevocationCheck, _config.FailOpenOnRevocation, out var warning))
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(warning))
            {
                _logger.Write(AgentLogger.Create(string.Empty, string.Empty, thumbprint, cert.Subject, 0, "Warning", warning));
            }

            return cert;
        }

        private bool ValidateAntiReplay(AgentRequest request, string clientThumbprint, out string error)
        {
            error = string.Empty;
            if (request.TimestampUnixSeconds <= 0 || string.IsNullOrWhiteSpace(request.Nonce) || string.IsNullOrWhiteSpace(request.CorrelationId))
            {
                error = "ReplayMetadataMissing";
                return false;
            }

            var requestTime = DateTimeOffset.FromUnixTimeSeconds(request.TimestampUnixSeconds);
            var skew = Math.Abs((DateTimeOffset.UtcNow - requestTime).TotalSeconds);
            if (skew > _config.RequestClockSkewSeconds)
            {
                error = "RequestExpired";
                return false;
            }

            if (!_nonceCache.TryAdd(clientThumbprint, request.Nonce, requestTime))
            {
                error = "ReplayDetected";
                return false;
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
                warning = "Revocation status could not be verified; fail-open enabled.";
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
                throw new InvalidOperationException("Agent TLS certificate not found in LocalMachine\\My.");
            }
        }

        private static void EnsureClientAllowList(string[] thumbprints)
        {
            if (thumbprints == null || thumbprints.Length == 0)
            {
                throw new InvalidOperationException("AnalyzerClientThumbprints must include at least one thumbprint.");
            }
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

    internal sealed class NonceCache
    {
        private readonly ConcurrentDictionary<string, DateTimeOffset> _entries = new ConcurrentDictionary<string, DateTimeOffset>();
        private readonly TimeSpan _expiration;
        private DateTimeOffset _lastCleanup = DateTimeOffset.MinValue;

        public NonceCache(TimeSpan expiration)
        {
            _expiration = expiration;
        }

        public bool TryAdd(string clientThumbprint, string nonce, DateTimeOffset timestamp)
        {
            var key = $"{clientThumbprint ?? string.Empty}:{nonce}";
            if (!_entries.TryAdd(key, timestamp))
            {
                return false;
            }

            CleanupIfNeeded();
            return true;
        }

        private void CleanupIfNeeded()
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _lastCleanup < TimeSpan.FromMinutes(1))
            {
                return;
            }

            foreach (var entry in _entries)
            {
                if (now - entry.Value > _expiration)
                {
                    _entries.TryRemove(entry.Key, out _);
                }
            }

            _lastCleanup = now;
        }
    }

    internal sealed class SlidingWindowRateLimiter
    {
        private readonly int _maxRequests;
        private readonly TimeSpan _window;
        private readonly int _burstLimit;
        private readonly TimeSpan _burstWindow;
        private readonly TimeSpan _backoff;
        private readonly ConcurrentDictionary<string, RateLimitState> _requests =
            new ConcurrentDictionary<string, RateLimitState>(StringComparer.OrdinalIgnoreCase);

        public SlidingWindowRateLimiter(int maxRequests, TimeSpan window, int burstLimit, TimeSpan burstWindow, TimeSpan backoff)
        {
            _maxRequests = maxRequests;
            _window = window;
            _burstLimit = burstLimit;
            _burstWindow = burstWindow;
            _backoff = backoff;
        }

        public RateLimitResult TryAcquire(string key)
        {
            if (_maxRequests <= 0 && _burstLimit <= 0)
            {
                return RateLimitResult.CreateAllowed();
            }

            var bucket = _requests.GetOrAdd(key ?? string.Empty, _ => new RateLimitState());
            var now = DateTimeOffset.UtcNow;

            lock (bucket)
            {
                if (bucket.BackoffUntil > now)
                {
                    return RateLimitResult.CreateThrottled((int)Math.Ceiling((bucket.BackoffUntil - now).TotalSeconds));
                }

                while (bucket.WindowRequests.Count > 0 && now - bucket.WindowRequests.Peek() > _window)
                {
                    bucket.WindowRequests.Dequeue();
                }

                while (bucket.BurstRequests.Count > 0 && now - bucket.BurstRequests.Peek() > _burstWindow)
                {
                    bucket.BurstRequests.Dequeue();
                }

                if (_maxRequests > 0 && bucket.WindowRequests.Count >= _maxRequests)
                {
                    bucket.BackoffUntil = now.Add(_backoff);
                    return RateLimitResult.CreateThrottled((int)Math.Ceiling(_backoff.TotalSeconds));
                }

                if (_burstLimit > 0 && bucket.BurstRequests.Count >= _burstLimit)
                {
                    bucket.BackoffUntil = now.Add(_backoff);
                    return RateLimitResult.CreateThrottled((int)Math.Ceiling(_backoff.TotalSeconds));
                }

                bucket.WindowRequests.Enqueue(now);
                bucket.BurstRequests.Enqueue(now);
                return RateLimitResult.CreateAllowed();
            }
        }
    }

    internal sealed class RateLimitState
    {
        public Queue<DateTimeOffset> WindowRequests { get; } = new Queue<DateTimeOffset>();
        public Queue<DateTimeOffset> BurstRequests { get; } = new Queue<DateTimeOffset>();
        public DateTimeOffset BackoffUntil { get; set; }
    }

    internal readonly struct RateLimitResult
    {
        public bool Allowed { get; }
        public int RetryAfterSeconds { get; }

        private RateLimitResult(bool allowed, int retryAfterSeconds)
        {
            Allowed = allowed;
            RetryAfterSeconds = retryAfterSeconds;
        }

        public static RateLimitResult CreateAllowed() => new RateLimitResult(true, 0);

        public static RateLimitResult CreateThrottled(int retryAfterSeconds) =>
            new RateLimitResult(false, Math.Max(retryAfterSeconds, 0));
    }
}
