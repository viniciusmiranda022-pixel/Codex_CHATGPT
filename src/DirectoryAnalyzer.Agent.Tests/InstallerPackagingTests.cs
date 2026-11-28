using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DirectoryAnalyzer.Agent.Tests
{
    /// <summary>
    /// Confere, sem WiX nem Windows, que o MSI (src/Installer) empacota o agente que abre a conexão de
    /// saída com o Broker, com todas as dependências da saída real do build net48, e que nada no pacote
    /// configura ou abre uma porta de entrada. A instalação em si continua dependendo de Windows/WiX.
    /// </summary>
    [TestClass]
    public class InstallerPackagingTests
    {
        private static readonly XNamespace Wix = "http://schemas.microsoft.com/wix/2006/wi";
        private static readonly XNamespace MsBuild = "http://schemas.microsoft.com/developer/msbuild/2003";
        private const string WixTargetDirVar = "$(var.DirectoryAnalyzer.Agent.TargetDir)";

        // Saída do build que não deve ir para o MSI, com o motivo.
        private static readonly Dictionary<string, string> ExcludedOutputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["System.Management.Automation.dll"] = "assembly de referência (Microsoft.PowerShell.5.ReferenceAssemblies); o Windows fornece o real",
        };

        private static string Metadata(string key)
        {
            var value = typeof(InstallerPackagingTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(a => a.Key == key).Value;
            return Path.GetFullPath(value.Replace('\\', Path.DirectorySeparatorChar));
        }

        private static string SrcDir => Metadata("RepoSrcDir");
        private static string AgentOutputDir => Metadata("AgentOutputDir");
        private static XDocument ProductWxs => XDocument.Load(Path.Combine(SrcDir, "Installer", "Product.wxs"));
        private static XDocument WixProj => XDocument.Load(Path.Combine(SrcDir, "Installer", "DirectoryAnalyzer.Agent.wixproj"));

        private static string Attr(XElement e, string name) => (string)e.Attribute(name) ?? string.Empty;

        [TestMethod]
        public void WixProject_ReferencesTheOutboundAgent_NotTheLegacyListenerService()
        {
            var references = WixProj.Descendants(MsBuild + "ProjectReference").Select(r => Attr(r, "Include").Replace('\\', '/')).ToList();

            CollectionAssert.AreEqual(new[] { "../DirectoryAnalyzer.Agent/DirectoryAnalyzer.Agent.csproj" }, references);
            Assert.IsFalse(WixProj.Descendants().Attributes().Any(a => a.Value.Contains("AgentService", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(WixProj.Descendants().Where(e => !e.HasElements).Any(e => e.Value.Contains("AgentService", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(ProductWxs.ToString().Contains("var.AgentService", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void PackagedExe_IsTheBrokerWorker_WithoutAnyInboundListener()
        {
            var exe = Path.Combine(AgentOutputDir, "DirectoryAnalyzer.Agent.exe");
            Assert.IsTrue(File.Exists(exe), "Saída net48 do agente não encontrada: " + exe);

            using var stream = File.OpenRead(exe);
            using var pe = new PEReader(stream);
            var md = pe.GetMetadataReader();

            var definedTypes = md.TypeDefinitions
                .Select(h => md.GetTypeDefinition(h))
                .Select(t => md.GetString(t.Namespace) + "." + md.GetString(t.Name))
                .ToList();
            CollectionAssert.Contains(definedTypes, "DirectoryAnalyzer.Agent.BrokerAgentWorker");
            CollectionAssert.Contains(definedTypes, "DirectoryAnalyzer.Agent.InventoryAgentService");
            CollectionAssert.DoesNotContain(definedTypes, "DirectoryAnalyzer.Agent.AgentHost");

            var referencedTypes = md.TypeReferences
                .Select(h => md.GetTypeReference(h))
                .Select(t => md.GetString(t.Namespace) + "." + md.GetString(t.Name))
                .ToList();
            foreach (var inbound in new[] { "System.Net.HttpListener", "System.Net.Sockets.TcpListener", "System.Net.Sockets.Socket" })
            {
                CollectionAssert.DoesNotContain(referencedTypes, inbound, "O agente não deve abrir porta de entrada.");
            }

            CollectionAssert.Contains(referencedTypes, "Microsoft.AspNetCore.SignalR.Client.HubConnectionBuilder");

            // AnyCPU sem 32BITPREF: em Windows x64 o serviço roda em 64 bits e lê HKLM na visão de 64 bits,
            // a mesma em que o MSI x64 grava a configuração.
            var flags = pe.PEHeaders.CorHeader.Flags;
            Assert.IsTrue(flags.HasFlag(CorFlags.ILOnly));
            Assert.IsFalse(flags.HasFlag(CorFlags.Requires32Bit));
            Assert.IsFalse(flags.HasFlag(CorFlags.Prefers32Bit));
        }

        [TestMethod]
        public void Msi_IsX64_SoRegistryViewMatchesTheAgent()
        {
            var package = ProductWxs.Descendants(Wix + "Package").Single();
            Assert.AreEqual("x64", Attr(package, "Platform"));
            Assert.AreEqual("x64", WixProj.Descendants(MsBuild + "Platform").First().Value.Trim());
            Assert.IsTrue(ProductWxs.Descendants(Wix + "Directory").Any(d => Attr(d, "Id") == "ProgramFiles64Folder"));
            Assert.IsFalse(ProductWxs.Descendants(Wix + "Directory").Any(d => Attr(d, "Id") == "ProgramFilesFolder"));
        }

        [TestMethod]
        public void Msi_PackagesEveryRuntimeFileOfTheAgentBuild_AndNothingElse()
        {
            Assert.IsTrue(Directory.Exists(AgentOutputDir), "Saída net48 do agente não encontrada: " + AgentOutputDir);

            var buildOutput = Directory.GetFiles(AgentOutputDir)
                .Select(Path.GetFileName)
                .Where(f => !f.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                .Where(f => !ExcludedOutputs.ContainsKey(f))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sources = ProductWxs.Descendants(Wix + "File").Select(f => Attr(f, "Source")).ToList();
            Assert.IsTrue(sources.All(s => s.StartsWith(WixTargetDirVar, StringComparison.Ordinal)),
                "Todo arquivo deve vir da saída de DirectoryAnalyzer.Agent: " + string.Join(", ", sources));
            var packaged = sources.Select(s => s.Substring(WixTargetDirVar.Length))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var missing = buildOutput.Except(packaged, StringComparer.OrdinalIgnoreCase).ToList();
            var extra = packaged.Except(buildOutput, StringComparer.OrdinalIgnoreCase).ToList();
            Assert.AreEqual(0, missing.Count, "Saída do build ausente do MSI: " + string.Join(", ", missing));
            Assert.AreEqual(0, extra.Count, "MSI referencia arquivo que o build não gera: " + string.Join(", ", extra));
            CollectionAssert.Contains(packaged, "DirectoryAnalyzer.Agent.exe.config", "Os binding redirects precisam ir junto do exe.");
            CollectionAssert.Contains(packaged, "Microsoft.AspNetCore.SignalR.Client.dll");
            CollectionAssert.Contains(packaged, "DirectoryAnalyzer.Collectors.dll");
            CollectionAssert.DoesNotContain(packaged, "System.Management.Automation.dll");
        }

        [TestMethod]
        public void Msi_ConfiguresOnlyTheOutboundConnection_AndNoInboundPortOrRule()
        {
            var xml = ProductWxs;
            var properties = xml.Descendants(Wix + "Property").Select(p => Attr(p, "Id")).ToList();
            foreach (var legacy in new[] { "LISTENPORT", "BINDPREFIX", "CERTTHUMBPRINT", "ALLOWEDCLIENTCERTTHUMBPRINTS", "TRUSTEDCATHUMBPRINTS", "CREATEFIREWALLRULE" })
            {
                CollectionAssert.DoesNotContain(properties, legacy);
            }

            CollectionAssert.Contains(properties, "BROKERURL");
            CollectionAssert.Contains(properties, "AGENTID");

            var registryNames = xml.Descendants(Wix + "RegistryValue").Select(r => Attr(r, "Name")).ToList();
            CollectionAssert.IsSubsetOf(new[] { "BrokerUrl", "AgentId", "BrokerClientCertificateThumbprint" }, registryNames);
            foreach (var legacy in new[] { "BindPrefix", "CertThumbprint", "AnalyzerClientThumbprints", "TrustedCaThumbprints" })
            {
                CollectionAssert.DoesNotContain(registryNames, legacy);
            }

            // A única ação de firewall REMOVE a regra de entrada deixada pela versão anterior; nenhuma cria regra.
            var commands = xml.Descendants(Wix + "CustomAction").Select(c => Attr(c, "ExeCommand")).Where(c => c.Length > 0).ToList();
            Assert.IsFalse(commands.Any(c => c.Contains("add rule", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(commands.Any(c => c.Contains("urlacl", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(commands.Any(c => c.Contains("powershell", StringComparison.OrdinalIgnoreCase)),
                "Propriedades do MSI não devem ser interpoladas em script.");
            Assert.IsTrue(commands.All(c => c.Contains("firewall delete rule", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(xml.Descendants(Wix + "FirewallException").Any());
        }

        [TestMethod]
        public void Msi_RequiresHttpsBrokerUrl_AndInstallsTheServiceTheAgentRunsAs()
        {
            var xml = ProductWxs;
            var condition = xml.Descendants(Wix + "Condition").Single(c => c.Parent?.Name == Wix + "Product").Value;
            StringAssert.Contains(condition, "BROKERURL ~<< \"https://\"");

            var service = xml.Descendants(Wix + "ServiceInstall").Single();
            Assert.AreEqual("DirectoryAnalyzerAgent", Attr(service, "Name"),
                "Deve coincidir com InventoryAgentService.ServiceName.");
            var serviceSource = File.ReadAllText(Path.Combine(SrcDir, "DirectoryAnalyzer.Agent", "InventoryAgentService.cs"));
            StringAssert.Contains(serviceSource, "ServiceName = \"DirectoryAnalyzerAgent\"");

            var account = xml.Descendants(Wix + "Property").Single(p => Attr(p, "Id") == "SERVICEACCOUNT");
            Assert.AreEqual(@"NT AUTHORITY\NetworkService", Attr(account, "Value"),
                "LocalService se apresenta como anônimo na rede e não lê o AD.");
        }

        [TestMethod]
        public void Msi_ConditionsUseTheUppercaseRemoveValue()
        {
            // O MSI define REMOVE="ALL"; a versão 1.0.0.0 comparava com "all" e nunca removia a regra.
            var conditions = ProductWxs.Descendants(Wix + "Custom").Select(c => c.Value).ToList();
            Assert.IsTrue(conditions.Count > 0);
            Assert.IsFalse(conditions.Any(c => c.Contains("REMOVE=\"all\"", StringComparison.Ordinal)));
        }

        [TestMethod]
        public void AgentSources_DoNotOpenInboundListeners()
        {
            var agentDir = Path.Combine(SrcDir, "DirectoryAnalyzer.Agent");
            var offenders = Directory.GetFiles(agentDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                .Where(f => File.ReadAllText(f).Contains("HttpListener") || File.ReadAllText(f).Contains("TcpListener"))
                .ToList();
            Assert.AreEqual(0, offenders.Count, string.Join(", ", offenders));
        }
    }
}
