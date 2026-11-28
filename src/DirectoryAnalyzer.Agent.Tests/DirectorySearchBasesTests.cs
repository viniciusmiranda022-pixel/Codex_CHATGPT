using System;
using System.Collections.Generic;
using DirectoryAnalyzer.Agent.Modules;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DirectoryAnalyzer.Agent.Tests
{
    /// <summary>
    /// Bases LDAP efetivamente consultadas por módulo, com um domínio filho fictício
    /// (origem.floresta.example) dentro de uma floresta fictícia (floresta.example).
    /// </summary>
    [TestClass]
    public class DirectorySearchBasesTests
    {
        private const string DomainNc = "DC=origem,DC=floresta,DC=example";
        private const string ForestNc = "DC=floresta,DC=example";

        private static RootDseSnapshot ChildDomainOfSyntheticForest() => new RootDseSnapshot
        {
            DefaultNamingContext = DomainNc,
            DomainDnsZones = "DC=DomainDnsZones," + DomainNc,
            ForestDnsZones = "DC=ForestDnsZones," + ForestNc,
            RootDomainNamingContext = ForestNc
        };

        [DataTestMethod]
        [DataRow("GetUsers", DomainNc)]
        [DataRow("GetGroups", DomainNc)]
        [DataRow("GetComputers", DomainNc)]
        [DataRow("GetGpos", "CN=Policies,CN=System," + DomainNc)]
        [DataRow("GetDnsZones", "CN=MicrosoftDNS,DC=DomainDnsZones," + DomainNc)]
        public void EachCatalogModule_QueriesOnlyItsDomainBase(string module, string expectedBase)
        {
            var bases = DirectorySearchBases.For(module, ChildDomainOfSyntheticForest());

            CollectionAssert.AreEqual(new[] { expectedBase }, new List<string>(bases));
            foreach (var searchBase in bases)
            {
                Assert.IsTrue(DirectorySearchBases.IsWithinDomain(searchBase, ChildDomainOfSyntheticForest()), searchBase);
                StringAssert.DoesNotMatch(searchBase, new System.Text.RegularExpressions.Regex("ForestDnsZones", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
            }
        }

        [TestMethod]
        public void DefaultCatalog_HasBaseForEveryModule_AndNoneLeavesTheDomain()
        {
            var rootDse = ChildDomainOfSyntheticForest();
            foreach (var module in ModuleCatalog.CreateDefault().ModuleNames)
            {
                foreach (var searchBase in DirectorySearchBases.For(module, rootDse))
                {
                    Assert.IsTrue(DirectorySearchBases.IsWithinDomain(searchBase, rootDse), $"{module}: {searchBase}");
                    Assert.IsFalse(string.Equals(searchBase, ForestNc, StringComparison.OrdinalIgnoreCase));
                }
            }
        }

        [TestMethod]
        public void DnsZones_IgnoresDomainDnsZonesAnnouncedOutsideTheDomain()
        {
            var rootDse = ChildDomainOfSyntheticForest();
            rootDse.DomainDnsZones = "DC=ForestDnsZones," + ForestNc;

            var bases = DirectorySearchBases.For("GetDnsZones", rootDse);

            CollectionAssert.AreEqual(new[] { "CN=MicrosoftDNS,DC=DomainDnsZones," + DomainNc }, new List<string>(bases));
        }

        [TestMethod]
        public void MissingDefaultNamingContext_StopsBeforeAnyQuery()
        {
            Assert.ThrowsException<InvalidOperationException>(() =>
                DirectorySearchBases.For("GetUsers", new RootDseSnapshot { ForestDnsZones = "DC=ForestDnsZones," + ForestNc }));
        }

        [TestMethod]
        public void ModuleWithoutDeclaredBase_IsNotQueried()
        {
            Assert.ThrowsException<ArgumentException>(() => DirectorySearchBases.For("TrustsAnalyzer", ChildDomainOfSyntheticForest()));
        }
    }
}
