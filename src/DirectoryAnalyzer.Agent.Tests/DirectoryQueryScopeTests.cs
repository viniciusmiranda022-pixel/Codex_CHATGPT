using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DirectoryAnalyzer.Agent.Modules;
using DirectoryAnalyzer.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DirectoryAnalyzer.Agent.Tests
{
    /// <summary>
    /// Escopo efetivo num domínio filho fictício (filho.origem.example) da floresta fictícia origem.example.
    /// O domínio informado na configuração fica vazio: o agente identifica o domínio pelo RootDSE.
    /// </summary>
    [TestClass]
    public class DirectoryQueryScopeTests
    {
        private const string ChildNc = "DC=filho,DC=origem,DC=example";
        private const string ParentNc = "DC=origem,DC=example";

        internal static RootDseSnapshot ChildDomainRootDse() => new RootDseSnapshot
        {
            DefaultNamingContext = ChildNc,
            DomainDnsZones = "DC=DomainDnsZones," + ChildNc,
            ForestDnsZones = "DC=ForestDnsZones," + ParentNc,
            RootDomainNamingContext = ParentNc
        };

        [TestMethod]
        public void ChildDomain_EffectiveDomainAndReferralPolicy()
        {
            var scope = DirectoryQueryScope.From(ChildDomainRootDse());

            Assert.AreEqual(ChildNc, scope.DomainNamingContext);
            Assert.AreEqual("filho.origem.example", scope.DomainDnsName);
            Assert.AreEqual("None", DirectoryQueryScope.ReferralPolicy);
            CollectionAssert.AreEqual(new[] { ChildNc }, scope.BasesFor("GetUsers").ToArray());
        }

        [DataTestMethod]
        [DataRow("GetUsers", "CN=usuario.ficticio01,CN=Users," + ChildNc, true)]
        [DataRow("GetUsers", "CN=a\\, b,OU=Ficticia,"+ ChildNc, true)]
        [DataRow("GetUsers", "CN=usuario.pai,CN=Users," + ParentNc, false)]
        [DataRow("GetUsers", "CN=usuario.irmao,CN=Users,DC=irmao,DC=origem,DC=example", false)]
        [DataRow("GetUsers", "CN=usuario.neto,CN=Users,DC=neto," + ChildNc, false)]
        [DataRow("GetUsers", "CN=registro,DC=DomainDnsZones," + ChildNc, false)]
        [DataRow("GetUsers", "CN=usuario,CN=Users,DC=filho,DC=origem,DC=example,DC=outra", false)]
        [DataRow("GetGpos", "CN={00000000-0000-0000-0000-000000000001},CN=Policies,CN=System," + ChildNc, true)]
        [DataRow("GetGpos", "CN=usuario.ficticio01,CN=Users," + ChildNc, false)]
        [DataRow("GetDnsZones", "DC=filho.origem.example,CN=MicrosoftDNS,DC=DomainDnsZones," + ChildNc, true)]
        [DataRow("GetDnsZones", "DC=origem.example,CN=MicrosoftDNS,DC=ForestDnsZones," + ParentNc, false)]
        public void Contains_AcceptsOnlyTheModuleBaseInsideTheChildDomain(string module, string dn, bool expected)
        {
            var scope = DirectoryQueryScope.From(ChildDomainRootDse());
            Assert.AreEqual(expected, scope.Contains(module, dn), dn);
        }

        [TestMethod]
        public void MissingNamingContext_StopsBeforeAnyQuery()
        {
            Assert.ThrowsException<InvalidOperationException>(() => DirectoryQueryScope.From(new RootDseSnapshot()));
            Assert.ThrowsException<InvalidOperationException>(() => DirectoryQueryScope.From(new RootDseSnapshot { DefaultNamingContext = "CN=Configuration,DC=origem,DC=example" }));
        }

        [TestMethod]
        public async Task ResultIdentifiesEffectiveDomain_AndDropsObjectsFromOtherDomains()
        {
            var identityWithoutDomain = new AgentIdentity("agente-filho-01", "SRV-FILHO-01", string.Empty);
            var executor = new ScopedExecutor(DirectoryQueryScope.From(ChildDomainRootDse()), new[]
            {
                "CN=usuario.ficticio01,CN=Users," + ChildNc,
                "CN=usuario.ficticio02,OU=Ficticia," + ChildNc,
                "CN=usuario.pai,CN=Users," + ParentNc,
                "CN=usuario.neto,CN=Users,DC=neto," + ChildNc
            });
            var processor = new AgentJobProcessor(ModuleCatalog.CreateDefault(), executor, identityWithoutDomain);

            var result = await processor.ProcessAsync("job-1", SyntheticData.UsersRequest("agente-filho-01"), null, CancellationToken.None);

            Assert.AreEqual("filho.origem.example", result.Domain, "O resultado identifica o domínio consultado, não a configuração vazia.");
            Assert.AreEqual(2, result.Items.Count);
            Assert.IsTrue(result.Items.All(item => item.Columns["DistinguishedName"].EndsWith("," + ChildNc)));
            Assert.AreEqual(1, result.Errors.Count);
            Assert.AreEqual(AgentJobProcessor.OutOfScopeCode, result.Errors[0].Code);
            StringAssert.Contains(result.Errors[0].Message, "2 objeto(s)");
            StringAssert.Contains(result.Summary, "Domínio: filho.origem.example");
            StringAssert.Contains(result.Summary, "Referências LDAP: None");
        }

        [TestMethod]
        public void ActionRegistry_PinsReferralPolicyOnEverySearcher()
        {
            // O ActionRegistry depende de System.DirectoryServices (Windows); aqui se confere o código-fonte.
            var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "DirectoryAnalyzer.Agent", "ActionRegistry.cs"));
            Assert.AreEqual(1, Regex.Matches(source, @"new DirectorySearcher\(").Count, "Todo DirectorySearcher deve vir de CreateSearcher.");
            StringAssert.Contains(source, "ReferralChasing = ReferralChasingOption.None");
            Assert.IsFalse(Regex.IsMatch(source, @"DirectorySearchBases\.For\("), "As bases devem vir do escopo resolvido (DirectoryQueryScope).");
            Assert.AreEqual(5, Regex.Matches(source, @"ResolveScope\(scope, config\)\.BasesFor\(").Count);
        }

        internal static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "DirectoryAnalyzer.Agent", "ActionRegistry.cs")))
            {
                dir = dir.Parent;
            }

            Assert.IsNotNull(dir, "Raiz do repositório não encontrada.");
            return dir.FullName;
        }

        /// <summary>Executor sintético que devolve DNs fixos, como se uma referência tivesse sido seguida.</summary>
        internal sealed class ScopedExecutor : IModuleExecutor
        {
            private readonly DirectoryQueryScope _scope;
            private readonly string[] _dns;

            public ScopedExecutor(DirectoryQueryScope scope, string[] dns)
            {
                _scope = scope;
                _dns = dns;
            }

            public Task<ModuleExecutionOutcome> ExecuteAsync(ModuleDefinition module, IReadOnlyDictionary<string, string> parameters, CancellationToken token)
            {
                var rows = _dns
                    .Select((dn, index) => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
                    {
                        ["SamAccountName"] = $"usuario.ficticio{index:00}",
                        ["DistinguishedName"] = dn
                    })
                    .ToList();
                return Task.FromResult(ModuleExecutionOutcome.Success(rows, _scope));
            }
        }
    }
}
