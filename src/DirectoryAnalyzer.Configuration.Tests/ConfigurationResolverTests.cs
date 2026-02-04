using System;
using System.IO;
using DirectoryAnalyzer.Agent.Contracts.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DirectoryAnalyzer.Configuration.Tests
{
    [TestClass]
    public class ConfigurationResolverTests
    {
        [TestMethod]
        public void ResolveAgentConfig_MigratesLegacyToProgramData()
        {
            var root = CreateTempRoot();
            var programData = Path.Combine(root, "pd");
            var baseDir = Path.Combine(root, "base");
            Directory.CreateDirectory(programData);
            Directory.CreateDirectory(baseDir);

            var legacyDir = Path.Combine(programData, "DirectoryAnalyzer");
            Directory.CreateDirectory(legacyDir);
            var legacyPath = Path.Combine(legacyDir, "agentsettings.json");
            File.WriteAllText(legacyPath, "legacy");

            var policy = new PathPolicy(programData, baseDir);
            var resolver = new ConfigurationResolver(policy);
            var result = resolver.ResolveAgentConfig();

            Assert.AreEqual(ConfigurationSource.ProgramData, result.Source);
            Assert.AreEqual(MigrationStatus.Migrated, result.MigrationStatus);
            Assert.IsTrue(File.Exists(policy.ProgramDataAgentConfigPath));
            Assert.AreEqual("legacy", File.ReadAllText(policy.ProgramDataAgentConfigPath));
        }

        [TestMethod]
        public void ResolveAgentConfig_UsesProgramDataWhenBothExist()
        {
            var root = CreateTempRoot();
            var programData = Path.Combine(root, "pd");
            var baseDir = Path.Combine(root, "base");
            Directory.CreateDirectory(programData);
            Directory.CreateDirectory(baseDir);

            var newDir = Path.Combine(programData, "DirectoryAnalyzerAgent");
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "agentsettings.json"), "new");

            var legacyDir = Path.Combine(programData, "DirectoryAnalyzer");
            Directory.CreateDirectory(legacyDir);
            File.WriteAllText(Path.Combine(legacyDir, "agentsettings.json"), "legacy");

            var policy = new PathPolicy(programData, baseDir);
            var resolver = new ConfigurationResolver(policy);
            var result = resolver.ResolveAgentConfig();

            Assert.AreEqual(ConfigurationSource.ProgramData, result.Source);
            Assert.AreEqual(MigrationStatus.LegacyIgnored, result.MigrationStatus);
        }

        [TestMethod]
        public void ResolveAgentConfig_UsesBaseDirectoryWhenPresent()
        {
            var root = CreateTempRoot();
            var programData = Path.Combine(root, "pd");
            var baseDir = Path.Combine(root, "base");
            Directory.CreateDirectory(programData);
            Directory.CreateDirectory(baseDir);

            var basePath = Path.Combine(baseDir, "agentsettings.json");
            File.WriteAllText(basePath, "base");

            var policy = new PathPolicy(programData, baseDir);
            var resolver = new ConfigurationResolver(policy);
            var result = resolver.ResolveAgentConfig();

            Assert.AreEqual(ConfigurationSource.BaseDirectory, result.Source);
            Assert.AreEqual(basePath, result.SelectedPath);
        }

        [TestMethod]
        public void ResolveAgentConfig_ReturnsDefaultWhenMissing()
        {
            var root = CreateTempRoot();
            var programData = Path.Combine(root, "pd");
            var baseDir = Path.Combine(root, "base");
            Directory.CreateDirectory(programData);
            Directory.CreateDirectory(baseDir);

            var policy = new PathPolicy(programData, baseDir);
            var resolver = new ConfigurationResolver(policy);
            var result = resolver.ResolveAgentConfig();

            Assert.AreEqual(ConfigurationSource.DefaultCreated, result.Source);
            Assert.AreEqual(MigrationStatus.NotFound, result.MigrationStatus);
        }

        private static string CreateTempRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "diranalyzer_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }
    }
}
