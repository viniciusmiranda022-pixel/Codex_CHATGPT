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
            var result = await ui.RunModuleAsync("GetUsers", new Dictionary<string, string> { ["IncludeDisabled"] = "false" }, "operador.ficticio", cts.Token);

            Assert.IsNotNull(result, "A WPF deveria receber um ModuleResult.");
            Assert.AreEqual("GetUsers", result.ModuleName);
            Assert.AreEqual(SyntheticData.OriginAgentId, result.AgentId);
            Assert.AreEqual(SyntheticData.OriginDomain, result.Domain);
            Assert.AreEqual(0, result.Errors.Count);
            Assert.AreEqual(2, result.Items.Count);
            Assert.AreEqual("usuario.ficticio01", result.Items[0].Columns["SamAccountName"]);
            Assert.AreEqual(1, origin.Calls);
        }

        [TestMethod]
        public async Task InlineScriptFromWpf_IsRefusedBeforeExecution_AndStillReturnsModuleResult()
        {
            await using var factory = new WebApplicationFactory<Program>();
            var origin = new RecordingExecutor(SyntheticData.OriginDomain);
            await using var agent = await ConnectAgentAsync(factory, SyntheticData.Origin, origin);

            var ui = CreateUiService(factory, SyntheticData.OriginAgentId);
            using var cts = new CancellationTokenSource(Timeout);
            var result = await ui.RunPowerShellScriptAsync("ScheduledTasksAnalyzer", "Get-ScheduledTask", null, "operador.ficticio", cts.Token);

            Assert.IsNotNull(result);
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
        public async Task UnknownOrIncompatibleRequests_AreRefusedBeforeExecution(string module, string key, string value, string expectedCode)
        {
            await using var factory = new WebApplicationFactory<Program>();
            var origin = new RecordingExecutor(SyntheticData.OriginDomain);
            await using var agent = await ConnectAgentAsync(factory, SyntheticData.Origin, origin);

            var parameters = key == null ? null : new Dictionary<string, string> { [key] = value };
            var ui = CreateUiService(factory, SyntheticData.OriginAgentId);
            using var cts = new CancellationTokenSource(Timeout);
            var result = await ui.RunModuleAsync(module, parameters, "operador.ficticio", cts.Token);

            Assert.IsNotNull(result);
            Assert.AreEqual(0, origin.Calls);
            Assert.AreEqual(expectedCode, result.Errors[0].Code);
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

            // Mesmo fluxo de BrokerAgentWorker.HandleJobAsync: gate -> executor -> SubmitResult.
            connection.On<string, JobRequest>("DispatchJob", async (jobId, request) =>
            {
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

            return new SyntheticAgent(connection);
        }

        private sealed class SyntheticAgent : IAsyncDisposable
        {
            public SyntheticAgent(HubConnection connection)
            {
                Connection = connection;
            }

            public HubConnection Connection { get; }

            public ValueTask DisposeAsync() => Connection.DisposeAsync();
        }
    }
}
