using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DirectoryAnalyzer.Agent.Modules;
using DirectoryAnalyzer.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DirectoryAnalyzer.Agent.Tests
{
    [TestClass]
    public class ModuleRequestGateTests
    {
        private static ModuleRequestGate CreateGate() => new ModuleRequestGate(ModuleCatalog.CreateDefault(), SyntheticData.Origin);

        [TestMethod]
        public void Accepts_CatalogModule_WithDeclaredParameters_AndNormalizesValues()
        {
            var request = SyntheticData.UsersRequest(SyntheticData.OriginAgentId, new Dictionary<string, string>
            {
                ["includedisabled"] = " TRUE "
            });

            var decision = CreateGate().Evaluate(request);

            Assert.IsTrue(decision.Accepted, decision.ErrorMessage);
            Assert.AreEqual("GetUsers", decision.Module.Name);
            Assert.AreEqual(ModuleScope.AgentDomainDirectory, decision.Module.Scope);
            Assert.AreEqual("true", decision.Parameters["IncludeDisabled"]);
        }

        [DataTestMethod]
        [DataRow("GetGroups")]
        [DataRow("GetComputers")]
        [DataRow("GetGpos")]
        [DataRow("GetDnsZones")]
        public void Accepts_ParameterlessCatalogModules(string module)
        {
            var request = SyntheticData.UsersRequest(SyntheticData.OriginAgentId, new Dictionary<string, string>());
            request.ModuleName = module;

            Assert.IsTrue(CreateGate().Evaluate(request).Accepted);
        }

        [TestMethod]
        public void Rejects_ArbitraryScript()
        {
            var request = SyntheticData.UsersRequest(SyntheticData.OriginAgentId, new Dictionary<string, string>
            {
                ["Script"] = "Get-ChildItem C:\\ | Remove-Item -WhatIf",
                ["ModuleName"] = "GetUsers"
            });
            request.ModuleName = "RunPowerShellScript";

            AssertRejected(request, GateErrorCodes.ArbitraryScriptRejected);
        }

        [DataTestMethod]
        [DataRow("ScheduledTasksAnalyzer")]
        [DataRow("SmbSharesAnalyzer")]
        [DataRow("InstalledServicesAnalyzer")]
        [DataRow("LocalProfilesAnalyzer")]
        [DataRow("LocalSecurityPolicyAnalyzer")]
        [DataRow("IisAppPoolsAnalyzer")]
        [DataRow("ProxyAddressAnalyzer")]
        [DataRow("TrustsAnalyzer")]
        [DataRow("GpoAnalyzer")]
        [DataRow("DnsAnalyzer")]
        public void Rejects_ExistingFanOutModules_UntilLocalVariantIsDecided(string module)
        {
            var request = SyntheticData.UsersRequest(SyntheticData.OriginAgentId, new Dictionary<string, string>
            {
                ["AttributeName"] = "Name",
                ["AttributeValue"] = "SRV-FICTICIO-*"
            });
            request.ModuleName = module;

            AssertRejected(request, GateErrorCodes.ModuleNotLocal);
        }

        [TestMethod]
        public void Rejects_ModuleOutsideCatalog()
        {
            var request = SyntheticData.UsersRequest(SyntheticData.OriginAgentId);
            request.ModuleName = "ResetUserPasswords";
            AssertRejected(request, GateErrorCodes.UnknownModule);
        }

        [DataTestMethod]
        [DataRow("Get-Users")]
        [DataRow("GetUsers;Remove-Item")]
        [DataRow("")]
        [DataRow("   ")]
        public void Rejects_MalformedModuleName(string module)
        {
            var request = SyntheticData.UsersRequest(SyntheticData.OriginAgentId);
            request.ModuleName = module;
            AssertRejected(request, GateErrorCodes.InvalidModuleName);
        }

        [DataTestMethod]
        [DataRow("ComputerName", "SRV-DESTINO-01")]
        [DataRow("Domain", "destino.example")]
        [DataRow("Server", "dc01.destino.example")]
        [DataRow("Script", "whoami")]
        [DataRow("AttributeName", "Name")]
        public void Rejects_UndeclaredParameters_IncludingScopeOverrides(string key, string value)
        {
            var request = SyntheticData.UsersRequest(SyntheticData.OriginAgentId, new Dictionary<string, string> { [key] = value });
            AssertRejected(request, GateErrorCodes.UnknownParameter);
        }

        [TestMethod]
        public void Rejects_ParametersOnParameterlessModule()
        {
            var request = SyntheticData.UsersRequest(SyntheticData.OriginAgentId);
            request.ModuleName = "GetGroups";
            AssertRejected(request, GateErrorCodes.UnknownParameter);
        }

        [DataTestMethod]
        [DataRow("IncludeDisabled", "yes")]
        [DataRow("IncludeDisabled", "1")]
        public void Rejects_IncompatibleParameterValues(string key, string value)
        {
            var request = SyntheticData.UsersRequest(SyntheticData.OriginAgentId, new Dictionary<string, string> { [key] = value });
            AssertRejected(request, GateErrorCodes.InvalidParameter);
        }

        [TestMethod]
        public void Rejects_OversizedParameterValue()
        {
            var request = SyntheticData.UsersRequest(SyntheticData.OriginAgentId, new Dictionary<string, string>
            {
                ["IncludeDisabled"] = new string('t', 600)
            });
            AssertRejected(request, GateErrorCodes.InvalidParameter);
        }

        [TestMethod]
        public void Rejects_JobAddressedToAnotherAgent()
        {
            AssertRejected(SyntheticData.UsersRequest(SyntheticData.DestinationAgentId), GateErrorCodes.TargetMismatch);
        }

        [DataTestMethod]
        [DataRow("(department=Financeiro Ficticio)")]
        [DataRow("(objectClass=*)")]
        [DataRow("(cn=*))(|(cn=*)")]
        public void Rejects_LdapFilter_BecauseQueryExpressionIsFixedInAgent(string filter)
        {
            // Revisão da #49: o filtro LDAP livre saiu do contrato remoto; só IncludeDisabled é aceito.
            var request = SyntheticData.UsersRequest(SyntheticData.OriginAgentId, new Dictionary<string, string> { ["LdapFilter"] = filter });
            AssertRejected(request, GateErrorCodes.UnknownParameter);
        }

        [TestMethod]
        public void Rejects_TargetListContainingThisAgentAndAnother()
        {
            var request = SyntheticData.UsersRequest(SyntheticData.OriginAgentId);
            request.TargetAgentIds.Add(SyntheticData.DestinationAgentId);
            AssertRejected(request, GateErrorCodes.AmbiguousTarget);

            var duplicated = SyntheticData.UsersRequest(SyntheticData.OriginAgentId);
            duplicated.TargetAgentIds.Add(SyntheticData.OriginAgentId);
            AssertRejected(duplicated, GateErrorCodes.AmbiguousTarget);
        }

        [TestMethod]
        public void Catalog_GetUsersDeclaresOnlyIncludeDisabled()
        {
            Assert.IsTrue(ModuleCatalog.CreateDefault().TryGet("GetUsers", out var module));
            CollectionAssert.AreEquivalent(new[] { "IncludeDisabled" }, new List<string>(module.Parameters.Keys));
        }

        [TestMethod]
        public void Rejects_JobWithoutTarget()
        {
            var request = SyntheticData.UsersRequest(SyntheticData.OriginAgentId);
            request.TargetAgentIds.Clear();
            AssertRejected(request, GateErrorCodes.TargetMismatch);
        }

        [TestMethod]
        public void Rejects_UnknownContractVersion()
        {
            var request = SyntheticData.UsersRequest(SyntheticData.OriginAgentId);
            request.ContractVersion = "2.0";
            AssertRejected(request, GateErrorCodes.UnsupportedContract);
        }

        [TestMethod]
        public void Catalog_CannotRegisterScriptAction()
        {
            Assert.ThrowsException<ArgumentException>(() =>
                new ModuleCatalog(new[] { new ModuleDefinition("RunPowerShellScript", ModuleScope.LocalHost) }));
        }

        [TestMethod]
        public void DefaultCatalog_ContainsOnlyReadOnlyDirectoryModules()
        {
            var catalog = ModuleCatalog.CreateDefault();
            CollectionAssert.AreEquivalent(
                new[] { "GetUsers", "GetGroups", "GetComputers", "GetGpos", "GetDnsZones" },
                new List<string>(catalog.ModuleNames));
            foreach (var name in catalog.ModuleNames)
            {
                Assert.IsTrue(catalog.TryGet(name, out var module));
                Assert.IsTrue(module.ReadOnly);
            }
        }

        [TestMethod]
        public async Task Processor_DoesNotExecuteOrReportProgress_WhenRejected()
        {
            var executor = new RecordingExecutor(SyntheticData.OriginDomain);
            var processor = new AgentJobProcessor(ModuleCatalog.CreateDefault(), executor, SyntheticData.Origin);
            var progressCalls = 0;
            var request = SyntheticData.UsersRequest(SyntheticData.OriginAgentId, new Dictionary<string, string> { ["Script"] = "whoami" });
            request.ModuleName = "RunPowerShellScript";

            var result = await processor.ProcessAsync("job-1", request, (p, m) => { progressCalls++; return Task.CompletedTask; }, CancellationToken.None);

            Assert.AreEqual(0, executor.Calls);
            Assert.AreEqual(0, progressCalls);
            Assert.AreEqual(0, result.Items.Count);
            Assert.AreEqual(1, result.Errors.Count);
            Assert.AreEqual(GateErrorCodes.ArbitraryScriptRejected, result.Errors[0].Code);
            Assert.AreEqual(AgentJobProcessor.GateExceptionType, result.Errors[0].ExceptionType);
            Assert.AreEqual(SyntheticData.OriginAgentId, result.AgentId);
            Assert.IsNull(result.ModuleName, "O nome do módulo não é ecoado quando o pedido é recusado.");
        }

        [TestMethod]
        public async Task Processor_ExecutesAcceptedModule_WithNormalizedParametersOnly()
        {
            var executor = new RecordingExecutor(SyntheticData.OriginDomain);
            var processor = new AgentJobProcessor(ModuleCatalog.CreateDefault(), executor, SyntheticData.Origin);

            var result = await processor.ProcessAsync("job-2", SyntheticData.UsersRequest(SyntheticData.OriginAgentId), null, CancellationToken.None);

            Assert.AreEqual(1, executor.Calls);
            Assert.AreEqual(1, executor.LastParameters.Count);
            Assert.AreEqual("false", executor.LastParameters["IncludeDisabled"]);
            Assert.AreEqual("GetUsers", result.ModuleName);
            Assert.AreEqual("corr-sintetico", result.CorrelationId);
            Assert.AreEqual(SyntheticData.OriginDomain, result.Domain);
            Assert.AreEqual(2, result.Items.Count);
            Assert.AreEqual(0, result.Errors.Count);
        }

        private static void AssertRejected(JobRequest request, string expectedCode)
        {
            var decision = CreateGate().Evaluate(request);
            Assert.IsFalse(decision.Accepted, "O pedido deveria ter sido recusado.");
            Assert.AreEqual(expectedCode, decision.ErrorCode, decision.ErrorMessage);
            Assert.IsNull(decision.Parameters);
        }
    }
}
