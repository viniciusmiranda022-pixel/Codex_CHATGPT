using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DirectoryAnalyzer.Agent.Modules;
using DirectoryAnalyzer.Contracts;
using DirectoryAnalyzer.Services;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DirectoryAnalyzer.Agent.Tests
{
    /// <summary>
    /// Fluxo completo com o Broker real em memória: BrokerJobService (código da WPF) -> POST /api/jobs ->
    /// SignalR DispatchJob -> agente sintético (conexão iniciada pelo agente) -> AgentJobProcessor ->
    /// SubmitResult -> ModuleResult de volta ao BrokerJobService.
    /// </summary>
    [TestClass]
    public class EndToEndFlowTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        [TestMethod]
        public async Task ValidCollection_ReturnsModuleResultToWpfService()
        {
            await using var factory = new WebApplicationFactory<Program>();
            var origin = new RecordingExecutor(SyntheticData.OriginDomain);
            await using var agent = await ConnectAgentAsync(factory, SyntheticData.Origin, origin);

            var ui = CreateUiService(factory, SyntheticData.OriginAgentId);
            using var cts = new CancellationTokenSource(Timeout);
            var outcome = await ui.ExecuteModuleAsync("GetUsers", new Dictionary<string, string> { ["IncludeDisabled"] = "false" }, "operador.ficticio", cts.Token);

            Assert.AreEqual(CollectionOutcomeKind.Completed, outcome.Kind, outcome.Message);
            Assert.AreEqual("Concluído", outcome.DashboardStatus);
            var result = outcome.Result;
            Assert.IsNotNull(result, "A WPF deveria receber um ModuleResult.");
            Assert.AreEqual("GetUsers", result.ModuleName);
            Assert.AreEqual(SyntheticData.OriginAgentId, result.AgentId);
            Assert.AreEqual(SyntheticData.OriginDomain, result.Domain);
            Assert.AreEqual(0, result.Errors.Count);
            Assert.AreEqual(2, result.Items.Count);
            Assert.AreEqual("usuario.ficticio01", result.Items[0].Columns["SamAccountName"]);
            Assert.AreEqual(1, origin.Calls);
            Assert.AreEqual(1, agent.DispatchCount);
        }

        [TestMethod]
        public async Task InlineScriptScreens_AreFlaggedInWpf_WithoutSendingAnything()
        {
            await using var factory = new WebApplicationFactory<Program>();
            var origin = new RecordingExecutor(SyntheticData.OriginDomain);
            await using var agent = await ConnectAgentAsync(factory, SyntheticData.Origin, origin);
            var ui = CreateUiService(factory, SyntheticData.OriginAgentId);
            using var cts = new CancellationTokenSource(Timeout);

            foreach (var module in ModuleAvailability.PendingMigrationModules)
            {
                Assert.IsTrue(ModuleAvailability.TryGetUnavailable(module, out var flagged), module);
                Assert.AreEqual(CollectionOutcomeKind.ModuleUnavailable, flagged.Kind);
                Assert.AreEqual("Indisponível nesta versão", flagged.DashboardStatus);

                var thrown = await Assert.ThrowsExceptionAsync<CollectionNotExecutedException>(() =>
                    ui.RunPowerShellScriptAsync(module, "Get-ScheduledTask", null, "operador.ficticio", cts.Token));
                Assert.AreEqual(CollectionOutcomeKind.ModuleUnavailable, thrown.Outcome.Kind);
            }

            CollectionAssert.AreEquivalent(
                new List<string>(ModuleCatalog.PendingNonLocalModules),
                new List<string>(ModuleAvailability.PendingMigrationModules),
                "A lista da WPF deve espelhar os módulos que o agente recusa como não locais.");
            Assert.AreEqual(0, agent.DispatchCount, "Nenhuma mensagem pode chegar ao agente.");
            Assert.AreEqual(0, origin.Calls);
        }

        [TestMethod]
        public async Task InlineScriptSentDirectly_IsRefusedByAgentBeforeExecution()
        {
            await using var factory = new WebApplicationFactory<Program>();
            var origin = new RecordingExecutor(SyntheticData.OriginDomain);
            await using var agent = await ConnectAgentAsync(factory, SyntheticData.Origin, origin);

            var ui = CreateUiService(factory, SyntheticData.OriginAgentId);
            using var cts = new CancellationTokenSource(Timeout);
            // Pedido montado fora da WPF (ex.: cliente antigo) com a ação de script removida.
            var request = new JobRequest
            {
                ModuleName = "RunPowerShellScript",
                Parameters = new Dictionary<string, string> { ["Script"] = "Get-ScheduledTask", ["ModuleName"] = "ScheduledTasksAnalyzer" }
            };
            var outcome = await ui.ExecuteAsync(request, cts.Token);

            Assert.AreEqual(CollectionOutcomeKind.Rejected, outcome.Kind);
            Assert.AreEqual(GateErrorCodes.ArbitraryScriptRejected, outcome.Code);
            var result = outcome.Result;
            Assert.IsNotNull(result);
            Assert.AreEqual(1, agent.DispatchCount);
            Assert.AreEqual(0, origin.Calls, "Nada pode ser executado.");
            Assert.AreEqual(0, result.Items.Count);
            Assert.AreEqual(GateErrorCodes.ArbitraryScriptRejected, result.Errors[0].Code);
            Assert.AreNotEqual("ScheduledTasksAnalyzer", result.ModuleName, "O override de ModuleName via parâmetro foi removido.");
        }

        [DataTestMethod]
        [DataRow("ResetUserPasswords", null, null, GateErrorCodes.UnknownModule)]
        [DataRow("DnsAnalyzer", null, null, GateErrorCodes.ModuleNotLocal)]
        [DataRow("GetUsers", "IncludeDisabled", "talvez", GateErrorCodes.InvalidParameter)]
        [DataRow("GetUsers", "ComputerName", "SRV-DESTINO-01", GateErrorCodes.UnknownParameter)]
        [DataRow("GetUsers", "LdapFilter", "(objectClass=*)", GateErrorCodes.UnknownParameter)]
        public async Task UnknownOrIncompatibleRequests_AreRefusedBeforeExecution(string module, string key, string value, string expectedCode)
        {
            await using var factory = new WebApplicationFactory<Program>();
            var origin = new RecordingExecutor(SyntheticData.OriginDomain);
            await using var agent = await ConnectAgentAsync(factory, SyntheticData.Origin, origin);

            var parameters = key == null ? null : new Dictionary<string, string> { [key] = value };
            var ui = CreateUiService(factory, SyntheticData.OriginAgentId);
            using var cts = new CancellationTokenSource(Timeout);
            var outcome = await ui.ExecuteModuleAsync(module, parameters, "operador.ficticio", cts.Token);

            Assert.AreEqual(CollectionOutcomeKind.Rejected, outcome.Kind);
            Assert.AreEqual("Recusado", outcome.DashboardStatus);
            Assert.AreEqual(expectedCode, outcome.Code);
            Assert.AreEqual(expectedCode, outcome.Result.Errors[0].Code);
            Assert.AreEqual(0, origin.Calls);

            // A API compatível não devolve mais null/0 itens após recusa.
            var thrown = await Assert.ThrowsExceptionAsync<CollectionNotExecutedException>(() =>
                ui.RunModuleAsync(module, parameters, "operador.ficticio", cts.Token));
            Assert.AreEqual(expectedCode, thrown.Outcome.Code);
            Assert.AreEqual(0, origin.Calls);
        }

        [TestMethod]
        public async Task EmptyCollection_AgentUnavailable_AndRejected_AreDistinctInWpfAndDashboard()
        {
            await using var factory = new WebApplicationFactory<Program>();
            var origin = new RecordingExecutor(SyntheticData.OriginDomain) { ReturnEmpty = true };
            await using var agent = await ConnectAgentAsync(factory, SyntheticData.Origin, origin);
            using var cts = new CancellationTokenSource(Timeout);

            var empty = await CreateUiService(factory, SyntheticData.OriginAgentId)
                .ExecuteModuleAsync("GetUsers", new Dictionary<string, string> { ["IncludeDisabled"] = "false" }, "operador.ficticio", cts.Token);
            var offline = await CreateUiService(factory, "agente-desconectado")
                .ExecuteModuleAsync("GetUsers", null, "operador.ficticio", cts.Token);
            var rejected = await CreateUiService(factory, SyntheticData.OriginAgentId)
                .ExecuteModuleAsync("GetUsers", new Dictionary<string, string> { ["LdapFilter"] = "(objectClass=*)" }, "operador.ficticio", cts.Token);
            var noTarget = await CreateUiService(factory, null)
                .ExecuteModuleAsync("GetUsers", null, "operador.ficticio", cts.Token);

            Assert.AreEqual(CollectionOutcomeKind.CompletedEmpty, empty.Kind);
            Assert.AreEqual(0, empty.ItemCount);
            Assert.AreEqual(SyntheticData.OriginAgentId, empty.Result.AgentId);
            Assert.AreEqual(CollectionOutcomeKind.AgentUnavailable, offline.Kind);
            Assert.AreEqual("AgentUnavailable", offline.Code);
            Assert.IsNull(offline.Result);
            Assert.AreEqual(CollectionOutcomeKind.Rejected, rejected.Kind);
            Assert.AreEqual(GateErrorCodes.UnknownParameter, rejected.Code);
            Assert.AreEqual(CollectionOutcomeKind.Rejected, noTarget.Kind);
            Assert.AreEqual("NoTargetConfigured", noTarget.Code);

            var dashboard = new[] { empty.DashboardStatus, offline.DashboardStatus, rejected.DashboardStatus };
            CollectionAssert.AreEqual(new[] { "Concluído (0 itens)", "Agente indisponível", "Recusado" }, dashboard);
            CollectionAssert.AllItemsAreUnique(dashboard);

            var screen = new[] { empty.DescribeForUser("usuários"), offline.DescribeForUser("usuários"), rejected.DescribeForUser("usuários") };
            CollectionAssert.AllItemsAreUnique(screen);
            StringAssert.Contains(screen[0], "nenhum usuários encontrado");
            StringAssert.StartsWith(screen[1], "🔌 Agente indisponível [AgentUnavailable]");
            StringAssert.StartsWith(screen[2], "⛔ Pedido recusado antes da execução [UnknownParameter]");
            Assert.IsTrue(empty.IsSuccess);
            Assert.IsFalse(offline.IsSuccess);
            Assert.IsFalse(rejected.IsSuccess);
            Assert.AreEqual(1, origin.Calls, "Só a coleta válida vazia chega ao executor.");
            Assert.AreEqual(2, agent.DispatchCount, "Pedido sem alvo e alvo offline não geram mensagem ao agente.");
        }

        [TestMethod]
        public async Task AgentDisconnectingMidJob_IsReportedAsAgentUnavailable()
        {
            await using var factory = new WebApplicationFactory<Program>();
            var origin = new RecordingExecutor(SyntheticData.OriginDomain) { Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously) };
            var agent = await ConnectAgentAsync(factory, SyntheticData.Origin, origin);
            using var cts = new CancellationTokenSource(Timeout);

            var pending = CreateUiService(factory, SyntheticData.OriginAgentId).ExecuteModuleAsync("GetUsers", null, "operador.ficticio", cts.Token);
            await WaitUntilAsync(() => origin.Calls == 1);
            await agent.DisposeAsync();
            var outcome = await pending;

            Assert.AreEqual(CollectionOutcomeKind.AgentUnavailable, outcome.Kind);
            Assert.AreEqual("AgentDisconnected", outcome.Code);
            Assert.AreEqual("Agente indisponível", outcome.DashboardStatus);
            origin.Gate.SetResult(true);
        }

        [TestMethod]
        public async Task DuplicateAgentId_BlocksDispatch_AndNoConnectionReceivesTheJob()
        {
            await using var factory = new WebApplicationFactory<Program>();
            var first = new RecordingExecutor(SyntheticData.OriginDomain);
            var second = new RecordingExecutor(SyntheticData.OriginDomain);
            await using var agentA = await ConnectAgentAsync(factory, SyntheticData.Origin, first);
            await using var agentB = await ConnectAgentAsync(factory, SyntheticData.Origin, second);

            using var http = factory.CreateClient();
            var response = await http.PostAsJsonAsync("/api/jobs", SyntheticData.UsersRequest(SyntheticData.OriginAgentId));
            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "AmbiguousTarget");

            using var cts = new CancellationTokenSource(Timeout);
            var outcome = await CreateUiService(factory, SyntheticData.OriginAgentId).ExecuteModuleAsync("GetUsers", null, "operador.ficticio", cts.Token);
            Assert.AreEqual(CollectionOutcomeKind.Rejected, outcome.Kind);
            Assert.AreEqual("AmbiguousTarget", outcome.Code);

            await Task.Delay(300);
            Assert.AreEqual(0, agentA.DispatchCount, "Despacho ambíguo não pode chegar à primeira conexão.");
            Assert.AreEqual(0, agentB.DispatchCount, "Despacho ambíguo não pode chegar à segunda conexão.");
            Assert.AreEqual(0, first.Calls + second.Calls);
        }

        [TestMethod]
        public async Task OnlyTheDispatchedConnection_CanUpdateTheJob()
        {
            await using var factory = new WebApplicationFactory<Program>();
            var designated = new RecordingExecutor(SyntheticData.OriginDomain) { Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously) };
            await using var agentA = await ConnectAgentAsync(factory, SyntheticData.Origin, designated);

            using var http = factory.CreateClient();
            var created = await http.PostAsJsonAsync("/api/jobs", SyntheticData.UsersRequest(SyntheticData.OriginAgentId));
            Assert.AreEqual(HttpStatusCode.OK, created.StatusCode);
            var job = await created.Content.ReadFromJsonAsync<JobStatus>();
            await WaitUntilAsync(() => designated.Calls == 1);

            // Depois do despacho, outra conexão declara o mesmo AgentId e tenta atualizar o job.
            var impostorExecutor = new RecordingExecutor(SyntheticData.DestinationDomain);
            await using var agentB = await ConnectAgentAsync(factory, SyntheticData.Origin, impostorExecutor);
            await agentB.Connection.InvokeAsync("ProgressUpdate", job.JobId, 99, "forjado");
            var forged = new ModuleResult { ModuleName = "GetUsers", Summary = "forjado" };
            forged.Items.Add(new ResultItem { Columns = { ["SamAccountName"] = "intruso.ficticio" } });
            await agentB.Connection.InvokeAsync("SubmitResult", job.JobId, forged);

            var status = await http.GetFromJsonAsync<JobStatus>($"/api/jobs/{job.JobId}");
            Assert.AreNotEqual("forjado", status.Message);
            Assert.AreNotEqual(99, status.ProgressPercent);
            Assert.AreEqual(HttpStatusCode.NotFound, (await http.GetAsync($"/api/jobs/{job.JobId}/result")).StatusCode);

            designated.Gate.SetResult(true);
            var final = await WaitForResultAsync(http, job.JobId);
            Assert.AreEqual("usuario.ficticio01", final.Items[0].Columns["SamAccountName"]);
            Assert.AreEqual(SyntheticData.OriginDomain, final.Domain);
            Assert.AreEqual(1, agentA.DispatchCount);
            Assert.AreEqual(0, agentB.DispatchCount);
            Assert.AreEqual(0, impostorExecutor.Calls);
        }

        [TestMethod]
        public async Task OriginJob_RunsOnlyOnOrigin_AndDestinationCannotOverwriteResult()
        {
            await using var factory = new WebApplicationFactory<Program>();
            var originExecutor = new RecordingExecutor(SyntheticData.OriginDomain) { Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously) };
            var destinationExecutor = new RecordingExecutor(SyntheticData.DestinationDomain);
            await using var originAgent = await ConnectAgentAsync(factory, SyntheticData.Origin, originExecutor);
            await using var destinationAgent = await ConnectAgentAsync(factory, SyntheticData.Destination, destinationExecutor);

            using var http = factory.CreateClient();
            var created = await http.PostAsJsonAsync("/api/jobs", SyntheticData.UsersRequest(SyntheticData.OriginAgentId));
            Assert.AreEqual(HttpStatusCode.OK, created.StatusCode);
            var status = await created.Content.ReadFromJsonAsync<JobStatus>();

            // O destino tenta entregar um resultado forjado para o job da origem.
            var forged = new ModuleResult { ModuleName = "GetUsers", AgentId = SyntheticData.OriginAgentId, Summary = "forjado" };
            forged.Items.Add(new ResultItem { Columns = { ["SamAccountName"] = "intruso.ficticio" } });
            await destinationAgent.Connection.InvokeAsync("SubmitResult", status.JobId, forged);

            var interim = await http.GetAsync($"/api/jobs/{status.JobId}/result");
            Assert.AreEqual(HttpStatusCode.NotFound, interim.StatusCode, "Resultado de agente não designado deve ser ignorado.");

            originExecutor.Gate.SetResult(true);
            var final = await WaitForResultAsync(http, status.JobId);

            Assert.AreEqual(SyntheticData.OriginAgentId, final.AgentId);
            Assert.AreEqual(SyntheticData.OriginDomain, final.Domain);
            Assert.AreEqual("usuario.ficticio01", final.Items[0].Columns["SamAccountName"]);
            Assert.AreEqual(1, originExecutor.Calls);
            Assert.AreEqual(0, destinationExecutor.Calls, "O job da origem não pode chegar ao destino.");
        }

        [TestMethod]
        public async Task TwoEnvironments_KeepSeparateResults()
        {
            await using var factory = new WebApplicationFactory<Program>();
            var originExecutor = new RecordingExecutor(SyntheticData.OriginDomain);
            var destinationExecutor = new RecordingExecutor(SyntheticData.DestinationDomain);
            await using var originAgent = await ConnectAgentAsync(factory, SyntheticData.Origin, originExecutor);
            await using var destinationAgent = await ConnectAgentAsync(factory, SyntheticData.Destination, destinationExecutor);

            using var cts = new CancellationTokenSource(Timeout);
            var originTask = CreateUiService(factory, SyntheticData.OriginAgentId).RunModuleAsync("GetUsers", null, "operador.ficticio", cts.Token);
            var destinationTask = CreateUiService(factory, SyntheticData.DestinationAgentId).RunModuleAsync("GetUsers", null, "operador.ficticio", cts.Token);
            var results = await Task.WhenAll(originTask, destinationTask);

            Assert.AreEqual(SyntheticData.OriginAgentId, results[0].AgentId);
            Assert.AreEqual($"usuario.ficticio01@{SyntheticData.OriginDomain}", results[0].Items[0].Columns["UserPrincipalName"]);
            Assert.AreEqual(SyntheticData.DestinationAgentId, results[1].AgentId);
            Assert.AreEqual($"usuario.ficticio01@{SyntheticData.DestinationDomain}", results[1].Items[0].Columns["UserPrincipalName"]);
            Assert.AreEqual(1, originExecutor.Calls);
            Assert.AreEqual(1, destinationExecutor.Calls);
        }

        [TestMethod]
        public async Task Broker_RejectsJobsWithoutSingleConnectedTarget()
        {
            await using var factory = new WebApplicationFactory<Program>();
            await using var agent = await ConnectAgentAsync(factory, SyntheticData.Origin, new RecordingExecutor(SyntheticData.OriginDomain));
            using var http = factory.CreateClient();

            var noTarget = SyntheticData.UsersRequest(SyntheticData.OriginAgentId);
            noTarget.TargetAgentIds.Clear();
            Assert.AreEqual(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/jobs", noTarget)).StatusCode);

            var twoTargets = SyntheticData.UsersRequest(SyntheticData.OriginAgentId);
            twoTargets.TargetAgentIds.Add(SyntheticData.DestinationAgentId);
            Assert.AreEqual(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/jobs", twoTargets)).StatusCode);

            var offline = SyntheticData.UsersRequest("agente-desconectado");
            Assert.AreEqual(HttpStatusCode.Conflict, (await http.PostAsJsonAsync("/api/jobs", offline)).StatusCode);
            Assert.AreEqual(0, agent.DispatchCount, "Nenhum pedido recusado pelo Broker chega ao agente.");
        }

        private static BrokerJobService CreateUiService(WebApplicationFactory<Program> factory, string targetAgentId)
        {
            var settings = new BrokerClientSettings
            {
                BrokerBaseUrl = factory.Server.BaseAddress.ToString(),
                PollIntervalSeconds = 1,
                RequestTimeoutSeconds = 30,
                TargetAgentId = targetAgentId
            };

            return new BrokerJobService(settings, () => factory.Server.CreateHandler());
        }

        private static async Task<ModuleResult> WaitForResultAsync(HttpClient http, string jobId)
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (DateTime.UtcNow < deadline)
            {
                var response = await http.GetAsync($"/api/jobs/{jobId}/result");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<ModuleResult>();
                }

                await Task.Delay(100);
            }

            Assert.Fail("Resultado não chegou ao Broker.");
            return null;
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    Assert.Fail("Condição não atingida no tempo limite.");
                }

                await Task.Delay(50);
            }
        }

        private static async Task<SyntheticAgent> ConnectAgentAsync(WebApplicationFactory<Program> factory, AgentIdentity identity, IModuleExecutor executor)
        {
            var connection = new HubConnectionBuilder()
                .WithUrl(new Uri(factory.Server.BaseAddress, "agent-hub"), options =>
                {
                    options.Transports = HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                })
                .Build();

            var processor = new AgentJobProcessor(ModuleCatalog.CreateDefault(), executor, identity);
            var agent = new SyntheticAgent(connection);

            // Mesmo fluxo de BrokerAgentWorker.HandleJobAsync: gate -> executor -> SubmitResult.
            connection.On<string, JobRequest>("DispatchJob", async (jobId, request) =>
            {
                // Conta cada mensagem recebida antes do gate e do executor.
                agent.RecordDispatch();
                var result = await processor.ProcessAsync(
                    jobId,
                    request,
                    (percent, message) => connection.InvokeAsync("ProgressUpdate", jobId, percent, message),
                    CancellationToken.None);
                await connection.InvokeAsync("SubmitResult", jobId, result);
            });

            await connection.StartAsync();
            await connection.InvokeAsync("AgentConnect", new AgentDescriptor
            {
                AgentId = identity.AgentId,
                Host = identity.Host,
                Capabilities = new List<string>(ModuleCatalog.CreateDefault().ModuleNames)
            });

            return agent;
        }

        private sealed class SyntheticAgent : IAsyncDisposable
        {
            public SyntheticAgent(HubConnection connection)
            {
                Connection = connection;
            }

            private int _dispatchCount;

            public HubConnection Connection { get; }

            /// <summary>Mensagens DispatchJob recebidas por esta conexão.</summary>
            public int DispatchCount => Volatile.Read(ref _dispatchCount);

            public void RecordDispatch() => Interlocked.Increment(ref _dispatchCount);

            public ValueTask DisposeAsync() => Connection.DisposeAsync();
        }
    }
}
