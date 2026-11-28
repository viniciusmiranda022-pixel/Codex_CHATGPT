using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using DirectoryAnalyzer.Agent.Broker;
using DirectoryAnalyzer.Agent.Modules;
using DirectoryAnalyzer.Broker.Hubs;
using DirectoryAnalyzer.Broker.Stores;
using DirectoryAnalyzer.Contracts;
using DirectoryAnalyzer.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DirectoryAnalyzer.Agent.Tests
{
    /// <summary>
    /// Queda da conexão do agente em pontos determinados do despacho, com o Broker real em memória.
    /// Os ganchos (HookedRegistry/HookedJobStore) derrubam a conexão do lado do Broker exatamente entre
    /// duas etapas do POST /api/jobs e só deixam o controller seguir depois que o Hub terminou a limpeza.
    /// O agente usa AgentBrokerSession, o mesmo código do BrokerAgentWorker.
    /// </summary>
    [TestClass]
    public class BrokerResilienceTests
    {
        private static readonly TimeSpan Bounded = TimeSpan.FromSeconds(5);

        [TestMethod]
        public async Task DisconnectBetweenSelectionAndJobCreation_EndsJobOnce_AndReconnectedAgentGetsNewJob()
        {
            await using var broker = new HookedBroker();
            var executor = new RecordingExecutor(SyntheticData.OriginDomain);
            await using var agent = await SessionAgent.ConnectAsync(broker, SyntheticData.Origin, executor);
            var firstConnection = broker.CurrentConnection(SyntheticData.OriginAgentId);

            broker.Registry.AfterSelectOnce = connectionId => broker.AbortAndWaitForCleanup(connectionId, agent);
            var (outcome, elapsed) = await RunFromWpfAsync(broker);

            Assert.AreEqual(CollectionOutcomeKind.AgentUnavailable, outcome.Kind, outcome.Message);
            Assert.AreEqual("AgentUnavailable", outcome.Code);
            Assert.IsTrue(elapsed < Bounded, $"A WPF deveria receber o desfecho em tempo limitado; levou {elapsed}.");
            var job = broker.Jobs.CreatedJobIds.Single();
            Assert.AreEqual(1, broker.Jobs.FinishCount(job), "O job deve terminar exatamente uma vez.");
            Assert.AreEqual(JobState.Failed, broker.Jobs.GetStatus(job).State);
            Assert.AreEqual(0, agent.DispatchCount);
            Assert.AreEqual(0, executor.Calls);

            await AssertReconnectedAgentReceivesNewJobAsync(broker, agent, executor, firstConnection);
        }

        [TestMethod]
        public async Task DisconnectAfterJobCreation_EndsJobOnce_AndReconnectedAgentGetsNewJob()
        {
            await using var broker = new HookedBroker();
            var executor = new RecordingExecutor(SyntheticData.OriginDomain);
            await using var agent = await SessionAgent.ConnectAsync(broker, SyntheticData.Origin, executor);
            var firstConnection = broker.CurrentConnection(SyntheticData.OriginAgentId);

            // A varredura do Hub e a nova checagem do controller tentam encerrar o mesmo job.
            broker.Jobs.AfterCreateOnce = (jobId, connectionId) => broker.AbortAndWaitForCleanup(connectionId, agent);
            var (outcome, elapsed) = await RunFromWpfAsync(broker);

            Assert.AreEqual(CollectionOutcomeKind.AgentUnavailable, outcome.Kind, outcome.Message);
            Assert.IsTrue(elapsed < Bounded, $"A WPF deveria receber o desfecho em tempo limitado; levou {elapsed}.");
            var job = broker.Jobs.CreatedJobIds.Single();
            Assert.AreEqual(1, broker.Jobs.FinishCount(job), "Varredura e controller não podem encerrar o job duas vezes.");
            Assert.IsTrue(broker.Jobs.FinishAttempts(job) >= 2, "Os dois caminhos deveriam ter tentado encerrar o job.");
            Assert.AreEqual(AgentHub.AgentDisconnectedMessage, broker.Jobs.GetStatus(job).Message);
            Assert.AreEqual(0, agent.DispatchCount);

            await AssertReconnectedAgentReceivesNewJobAsync(broker, agent, executor, firstConnection);
        }

        [TestMethod]
        public async Task JobRequestedWhileHubCleansUp_IsRefusedInsteadOfStranded()
        {
            await using var broker = new HookedBroker();
            var executor = new RecordingExecutor(SyntheticData.OriginDomain);
            await using var agent = await SessionAgent.ConnectAsync(broker, SyntheticData.Origin, executor);
            var firstConnection = broker.CurrentConnection(SyntheticData.OriginAgentId);

            // O pedido chega depois de o Hub listar os jobs da conexão que caiu e antes de ele terminar.
            HttpResponseMessage response = null;
            broker.Jobs.AfterSweepListingOnce = connectionId =>
            {
                using var http = broker.CreateClient();
                response = Task.Run(() => http.PostAsJsonAsync("/api/jobs", SyntheticData.UsersRequest(SyntheticData.OriginAgentId))).GetAwaiter().GetResult();
            };
            broker.AbortAndWaitForCleanup(firstConnection, agent);

            Assert.IsNotNull(response);
            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "AgentUnavailable");
            Assert.IsFalse(
                broker.Jobs.CreatedJobIds.Any(id => !InMemoryJobStore.IsTerminal(broker.Jobs.GetStatus(id).State)),
                "Nenhum job pode ficar pendente vinculado à conexão que caiu.");

            await AssertReconnectedAgentReceivesNewJobAsync(broker, agent, executor, firstConnection);
        }

        [TestMethod]
        public async Task DisconnectWhileRunning_LateResultFromReconnectedAgentDoesNotReopenJob()
        {
            await using var broker = new HookedBroker();
            var executor = new RecordingExecutor(SyntheticData.OriginDomain) { Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously) };
            await using var agent = await SessionAgent.ConnectAsync(broker, SyntheticData.Origin, executor);
            var firstConnection = broker.CurrentConnection(SyntheticData.OriginAgentId);

            var ui = CreateUiService(broker);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var pending = ui.ExecuteModuleAsync("GetUsers", null, "operador.ficticio", cts.Token);
            await WaitUntilAsync(() => executor.Calls == 1);
            var job = broker.Jobs.CreatedJobIds.Single();

            broker.AbortAndWaitForCleanup(firstConnection, agent);
            var outcome = await pending;
            Assert.AreEqual(CollectionOutcomeKind.AgentUnavailable, outcome.Kind);
            Assert.AreEqual("AgentDisconnected", outcome.Code);

            // O agente reconecta e entrega o resultado atrasado pela conexão nova: deve ser ignorado.
            await WaitUntilAsync(() => agent.Session.Registrations == 2);
            executor.Gate.SetResult(true);
            await WaitUntilAsync(() => agent.SubmitAttempts == 1);
            await Task.Delay(200);

            using var http = broker.CreateClient();
            Assert.AreEqual(HttpStatusCode.NotFound, (await http.GetAsync($"/api/jobs/{job}/result")).StatusCode);
            Assert.AreEqual(JobState.Failed, broker.Jobs.GetStatus(job).State);
            Assert.AreEqual(1, broker.Jobs.FinishCount(job));
        }

        [TestMethod]
        public async Task ChildDomainCollection_ReachesWpfWithEffectiveDomain()
        {
            await using var broker = new HookedBroker();
            var scope = DirectoryQueryScope.From(DirectoryQueryScopeTests.ChildDomainRootDse());
            var executor = new DirectoryQueryScopeTests.ScopedExecutor(scope, new[]
            {
                "CN=usuario.ficticio01,CN=Users,DC=filho,DC=origem,DC=example",
                "CN=usuario.ficticio02,CN=Users,DC=filho,DC=origem,DC=example"
            });
            var identity = new AgentIdentity("agente-filho-01", "SRV-FILHO-01", string.Empty);
            await using var agent = await SessionAgent.ConnectAsync(broker, identity, executor);

            var (outcome, _) = await RunFromWpfAsync(broker, identity.AgentId);

            Assert.AreEqual(CollectionOutcomeKind.Completed, outcome.Kind, outcome.Message);
            Assert.AreEqual("filho.origem.example", outcome.Result.Domain);
            Assert.AreEqual(2, outcome.Result.Items.Count);
            StringAssert.Contains(outcome.Result.Summary, "Referências LDAP: None");
        }

        [TestMethod]
        public void RetryPolicy_NeverGivesUp()
        {
            var policy = new UnboundedRetryPolicy();
            foreach (var attempt in new long[] { 0, 1, 2, 3, 10, 1000 })
            {
                var delay = policy.NextRetryDelay(new RetryContext { PreviousRetryCount = attempt });
                Assert.IsNotNull(delay, $"Tentativa {attempt} não pode encerrar a reconexão.");
                Assert.IsTrue(delay <= UnboundedRetryPolicy.MaxDelay);
            }
        }

        private static async Task AssertReconnectedAgentReceivesNewJobAsync(HookedBroker broker, SessionAgent agent, RecordingExecutor executor, string firstConnection)
        {
            await WaitUntilAsync(() => agent.Session.Registrations == 2);
            var current = broker.CurrentConnection(SyntheticData.OriginAgentId);
            Assert.AreNotEqual(firstConnection, current, "Reconexão gera um ConnectionId novo no Broker.");

            var (outcome, _) = await RunFromWpfAsync(broker);
            Assert.AreEqual(CollectionOutcomeKind.Completed, outcome.Kind, outcome.Message);
            Assert.AreEqual(SyntheticData.OriginAgentId, outcome.Result.AgentId);
            Assert.AreEqual(1, agent.DispatchCount, "A conexão restabelecida recebe o novo job.");
            Assert.AreEqual(1, executor.Calls);
            var second = broker.Jobs.CreatedJobIds.Last();
            Assert.AreEqual(current, broker.Jobs.GetAssignedConnectionId(second));
            Assert.AreEqual(1, broker.Jobs.FinishCount(second));
        }

        private static async Task<(CollectionOutcome Outcome, TimeSpan Elapsed)> RunFromWpfAsync(HookedBroker broker, string target = SyntheticData.OriginAgentId)
        {
            var ui = CreateUiService(broker, target);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var watch = Stopwatch.StartNew();
            var outcome = await ui.ExecuteModuleAsync("GetUsers", new Dictionary<string, string> { ["IncludeDisabled"] = "false" }, "operador.ficticio", cts.Token);
            return (outcome, watch.Elapsed);
        }

        private static BrokerJobService CreateUiService(HookedBroker broker, string target = SyntheticData.OriginAgentId)
        {
            var settings = new BrokerClientSettings
            {
                BrokerBaseUrl = broker.Server.BaseAddress.ToString(),
                PollIntervalSeconds = 1,
                RequestTimeoutSeconds = 30,
                TargetAgentId = target
            };

            return new BrokerJobService(settings, () => broker.Server.CreateHandler());
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    Assert.Fail("Condição não atingida no tempo limite.");
                }

                await Task.Delay(25);
            }
        }

        /// <summary>Broker real com registro e store instrumentados e acesso às conexões do Hub.</summary>
        private sealed class HookedBroker : WebApplicationFactory<Program>
        {
            private readonly ConcurrentDictionary<string, HubCallerContext> _contexts = new ConcurrentDictionary<string, HubCallerContext>();

            public HookedRegistry Registry { get; } = new HookedRegistry(new InMemoryAgentRegistry());
            public HookedJobStore Jobs { get; } = new HookedJobStore(new InMemoryJobStore());

            /// <summary>Única conexão registrada no Broker para o AgentId.</summary>
            public string CurrentConnection(string agentId) => Registry.GetConnectionIdsForAgent(agentId).Single();

            /// <summary>Derruba a conexão no Broker e bloqueia até o Hub retirá-la do registro e varrer os jobs.</summary>
            public void AbortAndWaitForCleanup(string connectionId, SessionAgent agent)
            {
                Assert.IsTrue(_contexts.TryGetValue(connectionId, out var context), "Conexão desconhecida.");
                var swept = Jobs.SweepSignal(connectionId);
                agent.DropNetwork();
                context.Abort();
                Assert.IsTrue(swept.Wait(TimeSpan.FromSeconds(10)), "O Hub não concluiu a limpeza da conexão.");
                Assert.IsFalse(Registry.TryGetAgentByConnection(connectionId, out _), "A conexão deveria ter saído do registro.");
            }

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<IAgentRegistry>(Registry);
                    services.AddSingleton<IJobStore>(Jobs);
                    services.Configure<HubOptions>(options => options.AddFilter(new TrackingFilter(_contexts)));
                });
            }
        }

        private sealed class TrackingFilter : IHubFilter
        {
            private readonly ConcurrentDictionary<string, HubCallerContext> _contexts;

            public TrackingFilter(ConcurrentDictionary<string, HubCallerContext> contexts)
            {
                _contexts = contexts;
            }

            public Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
            {
                _contexts[context.Context.ConnectionId] = context.Context;
                return next(context);
            }
        }

        private sealed class HookedRegistry : IAgentRegistry
        {
            private readonly IAgentRegistry _inner;

            public HookedRegistry(IAgentRegistry inner)
            {
                _inner = inner;
            }

            /// <summary>Roda uma vez, logo depois de o controller escolher a conexão e antes de criar o job.</summary>
            public Action<string> AfterSelectOnce { get; set; }

            public IReadOnlyCollection<string> GetConnectionIdsForAgent(string agentId)
            {
                var connections = _inner.GetConnectionIdsForAgent(agentId);
                var hook = AfterSelectOnce;
                AfterSelectOnce = null;
                if (hook != null && connections.Count == 1)
                {
                    hook(connections.First());
                }

                return connections;
            }

            public void Register(string connectionId, AgentDescriptor agentInfo) => _inner.Register(connectionId, agentInfo);
            public void Unregister(string connectionId) => _inner.Unregister(connectionId);
            public IReadOnlyCollection<AgentDescriptor> GetAgents() => _inner.GetAgents();
            public bool TryGetAgent(string agentId, out AgentDescriptor agentInfo) => _inner.TryGetAgent(agentId, out agentInfo);
            public bool TryGetAgentByConnection(string connectionId, out AgentDescriptor agentInfo) => _inner.TryGetAgentByConnection(connectionId, out agentInfo);
            public IReadOnlyCollection<string> GetConnectionIdsForAgents(IReadOnlyCollection<string> agentIds) => _inner.GetConnectionIdsForAgents(agentIds);
            public IReadOnlyCollection<string> GetAllConnectionIds() => _inner.GetAllConnectionIds();
        }

        private sealed class HookedJobStore : IJobStore
        {
            private readonly IJobStore _inner;
            private readonly ConcurrentDictionary<string, int> _finishes = new ConcurrentDictionary<string, int>();
            private readonly ConcurrentDictionary<string, int> _attempts = new ConcurrentDictionary<string, int>();
            private readonly ConcurrentDictionary<string, ManualResetEventSlim> _sweeps = new ConcurrentDictionary<string, ManualResetEventSlim>();
            private readonly ConcurrentQueue<string> _created = new ConcurrentQueue<string>();

            public HookedJobStore(IJobStore inner)
            {
                _inner = inner;
            }

            /// <summary>Roda uma vez, logo depois de o job ser criado e antes de o controller despachá-lo.</summary>
            public Action<string, string> AfterCreateOnce { get; set; }

            public IReadOnlyList<string> CreatedJobIds => _created.ToList();

            public int FinishCount(string jobId) => _finishes.TryGetValue(jobId, out var count) ? count : 0;

            public int FinishAttempts(string jobId) => _attempts.TryGetValue(jobId, out var count) ? count : 0;

            public ManualResetEventSlim SweepSignal(string connectionId) => _sweeps.GetOrAdd(connectionId, _ => new ManualResetEventSlim(false));

            public string CreateJob(JobRequest request) => CreateJob(request, null);

            public string CreateJob(JobRequest request, string assignedConnectionId)
            {
                var jobId = _inner.CreateJob(request, assignedConnectionId);
                _created.Enqueue(jobId);
                var hook = AfterCreateOnce;
                AfterCreateOnce = null;
                hook?.Invoke(jobId, assignedConnectionId);
                return jobId;
            }

            /// <summary>Roda uma vez, depois de o Hub listar os jobs abertos da conexão que caiu.</summary>
            public Action<string> AfterSweepListingOnce { get; set; }

            public IReadOnlyCollection<string> ListOpenJobsForConnection(string connectionId)
            {
                var open = _inner.ListOpenJobsForConnection(connectionId);
                var hook = AfterSweepListingOnce;
                AfterSweepListingOnce = null;
                hook?.Invoke(connectionId);
                // Sinaliza depois que o Hub terminar de encerrar estes jobs (ver Release abaixo).
                _pendingSweep[connectionId] = open.Count;
                if (open.Count == 0)
                {
                    SweepSignal(connectionId).Set();
                }

                return open;
            }

            private readonly ConcurrentDictionary<string, int> _pendingSweep = new ConcurrentDictionary<string, int>();

            public bool TryFinish(string jobId, JobState terminalState, string message, Action beforeCommit = null)
            {
                _attempts.AddOrUpdate(jobId, 1, (_, count) => count + 1);
                var finished = _inner.TryFinish(jobId, terminalState, message, beforeCommit);
                if (finished)
                {
                    _finishes.AddOrUpdate(jobId, 1, (_, count) => count + 1);
                }

                var connection = _inner.GetAssignedConnectionId(jobId);
                if (connection != null && message == AgentHub.AgentDisconnectedMessage
                    && _pendingSweep.TryGetValue(connection, out var pending)
                    && _pendingSweep.TryUpdate(connection, pending - 1, pending) && pending - 1 == 0)
                {
                    SweepSignal(connection).Set();
                }

                return finished;
            }

            public string GetAssignedConnectionId(string jobId) => _inner.GetAssignedConnectionId(jobId);
            public JobStatus GetStatus(string jobId) => _inner.GetStatus(jobId);
            public bool TryUpdateStatus(string jobId, JobStatus status) => _inner.TryUpdateStatus(jobId, status);
            public JobRequest GetRequest(string jobId) => _inner.GetRequest(jobId);
            public IReadOnlyCollection<string> ListJobIds() => _inner.ListJobIds();
            public bool TryReportProgress(string jobId, int progressPercent, string message) => _inner.TryReportProgress(jobId, progressPercent, message);
        }

        /// <summary>Agente sintético sobre AgentBrokerSession, com reconexão automática imediata.</summary>
        private sealed class SessionAgent : IAsyncDisposable
        {
            private int _dispatchCount;
            private int _submitAttempts;

            private SessionAgent()
            {
            }

            public AgentBrokerSession Session { get; private set; }
            public int DispatchCount => Volatile.Read(ref _dispatchCount);
            public int SubmitAttempts => Volatile.Read(ref _submitAttempts);

            private System.Net.WebSockets.WebSocket _socket;

            /// <summary>Simula a queda de rede: o socket do agente é abortado sem handshake de fechamento.</summary>
            public void DropNetwork() => _socket?.Abort();

            public static async Task<SessionAgent> ConnectAsync(HookedBroker broker, AgentIdentity identity, IModuleExecutor executor)
            {
                var agent = new SessionAgent();
                var connection = new HubConnectionBuilder()
                    .WithUrl(new Uri(broker.Server.BaseAddress, "agent-hub"), options =>
                    {
                        // WebSocket pelo TestServer, como no agente instalado (transporte preferido do SignalR).
                        options.Transports = HttpTransportType.WebSockets;
                        options.SkipNegotiation = true;
                        options.WebSocketFactory = async (context, token) =>
                        {
                            var socket = await broker.Server.CreateWebSocketClient().ConnectAsync(context.Uri, token);
                            agent._socket = socket;
                            return socket;
                        };
                    })
                    .WithAutomaticReconnect(new UnboundedRetryPolicy(TimeSpan.FromMilliseconds(50)))
                    .Build();

                var processor = new AgentJobProcessor(ModuleCatalog.CreateDefault(), executor, identity);
                agent.Session = new AgentBrokerSession(
                    connection,
                    () => new AgentDescriptor
                    {
                        AgentId = identity.AgentId,
                        Host = identity.Host,
                        Capabilities = new List<string>(ModuleCatalog.CreateDefault().ModuleNames)
                    },
                    async (jobId, request) =>
                    {
                        Interlocked.Increment(ref agent._dispatchCount);
                        var result = await processor.ProcessAsync(
                            jobId,
                            request,
                            (percent, message) => SafeInvokeAsync(connection, "ProgressUpdate", jobId, percent, message),
                            CancellationToken.None);
                        Interlocked.Increment(ref agent._submitAttempts);
                        await SafeInvokeAsync(connection, "SubmitResult", jobId, result);
                    });

                await agent.Session.StartAsync(CancellationToken.None);
                return agent;
            }

            private static async Task SafeInvokeAsync(HubConnection connection, string method, params object[] args)
            {
                // Durante a reconexão a chamada pode falhar; o agente real registra e segue.
                for (var attempt = 0; attempt < 100; attempt++)
                {
                    if (connection.State == HubConnectionState.Connected)
                    {
                        try
                        {
                            await connection.InvokeCoreAsync(method, args);
                            return;
                        }
                        catch (Exception)
                        {
                        }
                    }

                    await Task.Delay(50);
                }
            }

            public ValueTask DisposeAsync() => Session.Connection.DisposeAsync();
        }
    }
}
