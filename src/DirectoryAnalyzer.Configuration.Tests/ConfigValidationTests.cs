using System;
using System.IO;
using DirectoryAnalyzer.Agent;
using DirectoryAnalyzer.Agent.Contracts;
using DirectoryAnalyzer.Agent.Contracts.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DirectoryAnalyzer.Configuration.Tests
{
    [TestClass]
    public class ConfigValidationTests
    {
        [TestMethod]
        public void AgentConfigValidator_FlagsMissingFields()
        {
            var config = new AgentConfig
            {
                BindPrefix = string.Empty,
                CertThumbprint = string.Empty,
                AnalyzerClientThumbprints = Array.Empty<string>(),
                LogPath = string.Empty
            };

            var errors = AgentConfigValidator.Validate(config);
            Assert.IsTrue(errors.Count >= 3);
        }

        [TestMethod]
        public void AnalyzerConfigValidator_FlagsInvalidEndpoint()
        {
            var config = new AnalyzerConfig
            {
                AgentEndpoint = "http://invalid",
                ClientCertThumbprint = "ZZZ",
                RequestTimeoutSeconds = 0
            };

            var errors = AnalyzerConfigValidator.Validate(config);
            Assert.IsTrue(errors.Count >= 2);
        }

        [TestMethod]
        public void AnalyzerConfigLoader_ReturnsErrorOnInvalidJson()
        {
            var path = Path.Combine(Path.GetTempPath(), "invalid_config.json");
            File.WriteAllText(path, "not-json");

            var ok = AnalyzerConfigLoader.TryLoad(path, out var config, out var error);

            Assert.IsFalse(ok);
            Assert.IsNull(config);
            Assert.IsFalse(string.IsNullOrWhiteSpace(error));
        }
    }
}
