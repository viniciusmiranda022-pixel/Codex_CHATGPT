using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using DirectoryAnalyzer.Agent.Contracts;
using DirectoryAnalyzer.Agent.Modules;
using DirectoryAnalyzer.Contracts;
using Microsoft.AspNetCore.SignalR.Client;

namespace DirectoryAnalyzer.Agent
{
    public sealed class BrokerAgentWorker
    {
        private readonly AgentConfig _config;
        private readonly AgentLogger _logger;
        private HubConnection _connection;
        private AgentJobProcessor _processor;

        public BrokerAgentWorker(AgentConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = new AgentLogger(_config.LogPath);
        }

        public async Task StartAsync(CancellationToken token)
        {
            var identity = new AgentIdentity(ResolveAgentId(), Environment.MachineName, _config.Domain);
            _processor = new AgentJobProcessor(
                ModuleCatalog.CreateDefault(),
                new ActionRegistryModuleExecutor(new ActionRegistry(), _config),
                identity);
            _connection = BuildConnection();
            _connection.On<string, JobRequest>("DispatchJob", async (jobId, request) =>
            {
                await HandleJobAsync(jobId, request, token).ConfigureAwait(false);
            });

            await _connection.StartAsync(token).ConfigureAwait(false);
            await _connection.InvokeAsync("AgentConnect", BuildDescriptor(), token).ConfigureAwait(false);
            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
        }

        public async Task StopAsync()
        {
            if (_connection != null)
            {
                await _connection.StopAsync().ConfigureAwait(false);
            }
        }

        private HubConnection BuildConnection()
        {
            IHubConnectionBuilder builder = new HubConnectionBuilder();
            builder = builder.WithUrl(_config.BrokerUrl, options =>
            {
                var cert = TryLoadClientCertificate();
                if (cert != null)
                {
                    options.ClientCertificates.Add(cert);
                }
            });

            builder = builder.WithAutomaticReconnect();
            return builder.Build();
        }

        private X509Certificate2 TryLoadClientCertificate()
        {
            if (string.IsNullOrWhiteSpace(_config.BrokerClientCertificateThumbprint))
            {
                return null;
            }

            var normalized = _config.BrokerClientCertificateThumbprint.Replace(" ", string.Empty);
            using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);
            var matches = store.Certificates.Find(X509FindType.FindByThumbprint, normalized, false);
            return matches.Count > 0 ? matches[0] : null;
        }

        private AgentDescriptor BuildDescriptor()
        {
            return new AgentDescriptor
            {
                AgentId = ResolveAgentId(),
                Host = Environment.MachineName,
                Version = typeof(BrokerAgentWorker).Assembly.GetName().Version?.ToString(),
                // Anuncia o catálogo efetivamente instalado, não uma lista configurável.
                Capabilities = ModuleCatalog.CreateDefault().ModuleNames.ToList()
            };
        }

        private async Task HandleJobAsync(string jobId, JobRequest request, CancellationToken token)
        {
            // O gate do agente decide antes de qualquer execução; recusas voltam como ModuleResult com ErrorInfo.
            var result = await _processor.ProcessAsync(
                jobId,
                request,
                (percent, message) => _connection.InvokeAsync("ProgressUpdate", jobId, percent, message, token),
                token).ConfigureAwait(false);

            await _connection.InvokeAsync("SubmitResult", jobId, result, token).ConfigureAwait(false);
        }

        private string ResolveAgentId()
        {
            return string.IsNullOrWhiteSpace(_config.AgentId) ? Environment.MachineName : _config.AgentId;
        }

        private sealed class ActionRegistryModuleExecutor : IModuleExecutor
        {
            private readonly ActionRegistry _registry;
            private readonly AgentConfig _config;

            public ActionRegistryModuleExecutor(ActionRegistry registry, AgentConfig config)
            {
                _registry = registry;
                _config = config;
            }

            public async Task<ModuleExecutionOutcome> ExecuteAsync(ModuleDefinition module, IReadOnlyDictionary<string, string> parameters, CancellationToken token)
            {
                var agentRequest = new AgentRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    ActionName = module.Name,
                    Parameters = parameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
                    TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Nonce = Guid.NewGuid().ToString("N")
                };

                var response = await _registry.ExecuteAsync(agentRequest, _config, token).ConfigureAwait(false);
                if (response.Status != AgentStatus.Success)
                {
                    return ModuleExecutionOutcome.Failure(response.Error?.Code, response.Error?.Message, response.Error?.Details);
                }

                var rows = ConvertPayloadToItems(response.Payload)
                    .Select(item => (IReadOnlyDictionary<string, string>)item.Columns)
                    .ToList();
                return ModuleExecutionOutcome.Success(rows);
            }

            private static IEnumerable<ResultItem> ConvertPayloadToItems(object payload)
            {
                if (payload == null)
                {
                    return Enumerable.Empty<ResultItem>();
                }

                if (payload is System.Collections.IDictionary dictionary)
                {
                    return new[] { BuildItemFromDictionary(dictionary) };
                }

                if (payload is System.Collections.IEnumerable enumerable && !(payload is string))
                {
                    return BuildItemsFromEnumerable(enumerable);
                }

                var collectionProperty = payload.GetType().GetProperties()
                    .FirstOrDefault(prop => prop.PropertyType != typeof(string)
                        && typeof(System.Collections.IEnumerable).IsAssignableFrom(prop.PropertyType));

                if (collectionProperty != null)
                {
                    var collection = collectionProperty.GetValue(payload) as System.Collections.IEnumerable;
                    if (collection != null)
                    {
                        return BuildItemsFromEnumerable(collection);
                    }
                }

                return new[] { BuildItemFromObject(payload) };
            }

            private static IEnumerable<ResultItem> BuildItemsFromEnumerable(System.Collections.IEnumerable items)
            {
                var list = new List<ResultItem>();
                foreach (var item in items)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    if (item is System.Collections.IDictionary dictionary)
                    {
                        list.Add(BuildItemFromDictionary(dictionary));
                    }
                    else
                    {
                        list.Add(BuildItemFromObject(item));
                    }
                }

                return list;
            }

            private static ResultItem BuildItemFromObject(object item)
            {
                var resultItem = new ResultItem { Severity = ResultSeverity.Info };
                foreach (var prop in item.GetType().GetProperties())
                {
                    if (!prop.CanRead)
                    {
                        continue;
                    }

                    var value = prop.GetValue(item);
                    resultItem.Columns[prop.Name] = value == null ? string.Empty : Convert.ToString(value);
                }

                return resultItem;
            }

            private static ResultItem BuildItemFromDictionary(System.Collections.IDictionary dictionary)
            {
                var resultItem = new ResultItem { Severity = ResultSeverity.Info };
                foreach (var key in dictionary.Keys)
                {
                    var keyText = Convert.ToString(key) ?? string.Empty;
                    var value = dictionary[key];
                    resultItem.Columns[keyText] = value == null ? string.Empty : Convert.ToString(value);
                }

                return resultItem;
            }
        }
    }
}
