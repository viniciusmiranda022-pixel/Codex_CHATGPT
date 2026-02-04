using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using DirectoryAnalyzer.Agent.Contracts.Services;

namespace DirectoryAnalyzer.Agent
{
    [DataContract]
    public sealed class AgentConfig
    {
        [DataMember(Order = 1)]
        public string BindPrefix { get; set; } = "https://+:8443/agent/";

        [DataMember(Order = 2)]
        public string CertThumbprint { get; set; } = string.Empty;

        [DataMember(Order = 3)]
        public string[] AnalyzerClientThumbprints { get; set; } = Array.Empty<string>();

        [DataMember(Order = 4)]
        public int ActionTimeoutSeconds { get; set; } = 30;

        [DataMember(Order = 5)]
        public string LogPath { get; set; } = DefaultLogPath();

        [DataMember(Order = 6)]
        public string Domain { get; set; } = string.Empty;

        [DataMember(Order = 7)]
        public int MaxRequestBytes { get; set; } = 65536;

        [DataMember(Order = 8)]
        public int RequestClockSkewSeconds { get; set; } = 300;

        [DataMember(Order = 9)]
        public int ReplayCacheMinutes { get; set; } = 10;

        [DataMember(Order = 10)]
        public bool RequireSignedRequests { get; set; } = true;

        [DataMember(Order = 11)]
        public int MaxRequestsPerMinute { get; set; } = 60;

        [DataMember(Order = 12)]
        public int MaxConcurrentRequests { get; set; } = 10;

        [DataMember(Order = 13)]
        public bool EnforceRevocationCheck { get; set; } = true;

        [DataMember(Order = 14)]
        public bool FailOpenOnRevocation { get; set; } = false;

        [DataMember(Order = 15)]
        public int MaxBurstRequests { get; set; } = 15;

        [DataMember(Order = 16)]
        public int BurstWindowSeconds { get; set; } = 10;

        [DataMember(Order = 17)]
        public int RateLimitBackoffSeconds { get; set; } = 15;

        public static string DefaultLogPath()
        {
            var policy = new PathPolicy();
            return policy.DefaultAgentLogPath;
        }
    }

    public sealed class AgentConfigLoadResult
    {
        public AgentConfig Config { get; set; }
        public IReadOnlyList<string> RegistryOverrides { get; set; } = Array.Empty<string>();
        public string LogPathSource { get; set; }
        public bool ConfigFileLoaded { get; set; }
    }

    public static class AgentConfigLoader
    {
        public static AgentConfigLoadResult Load(string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                var serializer = new DataContractJsonSerializer(typeof(AgentConfig));
                var config = serializer.ReadObject(stream) as AgentConfig;
                if (config == null)
                {
                    throw new InvalidOperationException("Invalid agent configuration.");
                }

                var overrides = ApplyRegistryOverrides(config, out var logPathSource);
                if (string.IsNullOrWhiteSpace(config.LogPath))
                {
                    config.LogPath = AgentConfig.DefaultLogPath();
                    if (string.IsNullOrWhiteSpace(logPathSource))
                    {
                        logPathSource = "Default";
                    }
                }

                return new AgentConfigLoadResult
                {
                    Config = config,
                    RegistryOverrides = overrides,
                    LogPathSource = string.IsNullOrWhiteSpace(logPathSource) ? "Config" : logPathSource,
                    ConfigFileLoaded = true
                };
            }

            var created = CreateFromRegistry(path, out var createOverrides, out var createdLogSource);
            return new AgentConfigLoadResult
            {
                Config = created,
                RegistryOverrides = createOverrides,
                LogPathSource = string.IsNullOrWhiteSpace(createdLogSource) ? "Default" : createdLogSource,
                ConfigFileLoaded = false
            };
        }

        public static bool TryLoad(string path, out AgentConfigLoadResult result, out string error)
        {
            try
            {
                result = Load(path);
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                result = null;
                error = ex.Message;
                return false;
            }
        }

        private static AgentConfig CreateFromRegistry(string path, out IReadOnlyList<string> overrides, out string logPathSource)
        {
            var config = new AgentConfig
            {
                BindPrefix = ReadRegistryValue("BindPrefix", "https://+:8443/agent/"),
                CertThumbprint = ReadRegistryValue("CertThumbprint", string.Empty),
                AnalyzerClientThumbprints = ReadRegistryValue("AnalyzerClientThumbprints", string.Empty)
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries),
                ActionTimeoutSeconds = ReadRegistryInt("ActionTimeoutSeconds", 30),
                LogPath = ReadRegistryValue("LogPath", AgentConfig.DefaultLogPath()),
                Domain = ReadRegistryValue("Domain", string.Empty),
                MaxRequestBytes = ReadRegistryInt("MaxRequestBytes", 65536),
                RequestClockSkewSeconds = ReadRegistryInt("RequestClockSkewSeconds", 300),
                ReplayCacheMinutes = ReadRegistryInt("ReplayCacheMinutes", 10),
                RequireSignedRequests = ReadRegistryBool("RequireSignedRequests", true),
                MaxRequestsPerMinute = ReadRegistryInt("MaxRequestsPerMinute", 60),
                MaxConcurrentRequests = ReadRegistryInt("MaxConcurrentRequests", 10),
                EnforceRevocationCheck = ReadRegistryBool("EnforceRevocationCheck", true),
                FailOpenOnRevocation = ReadRegistryBool("FailOpenOnRevocation", false),
                MaxBurstRequests = ReadRegistryInt("MaxBurstRequests", 15),
                BurstWindowSeconds = ReadRegistryInt("BurstWindowSeconds", 10),
                RateLimitBackoffSeconds = ReadRegistryInt("RateLimitBackoffSeconds", 15)
            };

            var applied = ApplyRegistryOverrides(config, out logPathSource);
            overrides = applied;

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppDomain.CurrentDomain.BaseDirectory);
            using var stream = File.Create(path);
            var serializer = new DataContractJsonSerializer(typeof(AgentConfig));
            serializer.WriteObject(stream, config);
            return config;
        }

        private static IReadOnlyList<string> ApplyRegistryOverrides(AgentConfig config, out string logPathSource)
        {
            var overrides = new List<string>();
            logPathSource = string.Empty;

            var bindPrefix = ReadRegistryValue("BindPrefix", null);
            if (!string.IsNullOrWhiteSpace(bindPrefix))
            {
                config.BindPrefix = bindPrefix;
                overrides.Add("BindPrefix");
            }

            var certThumbprint = ReadRegistryValue("CertThumbprint", null);
            if (!string.IsNullOrWhiteSpace(certThumbprint))
            {
                config.CertThumbprint = certThumbprint;
                overrides.Add("CertThumbprint");
            }

            var analyzerThumbprints = ReadRegistryValue("AnalyzerClientThumbprints", null);
            if (!string.IsNullOrWhiteSpace(analyzerThumbprints))
            {
                config.AnalyzerClientThumbprints = analyzerThumbprints
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                overrides.Add("AnalyzerClientThumbprints");
            }

            var logPath = ReadRegistryValue("LogPath", null);
            if (!string.IsNullOrWhiteSpace(logPath))
            {
                config.LogPath = logPath;
                overrides.Add("LogPath");
                logPathSource = "Registry";
            }

            var domain = ReadRegistryValue("Domain", null);
            if (domain != null)
            {
                config.Domain = domain;
                overrides.Add("Domain");
            }

            config.ActionTimeoutSeconds = ReadRegistryInt("ActionTimeoutSeconds", config.ActionTimeoutSeconds);
            config.MaxRequestBytes = ReadRegistryInt("MaxRequestBytes", config.MaxRequestBytes);
            config.RequestClockSkewSeconds = ReadRegistryInt("RequestClockSkewSeconds", config.RequestClockSkewSeconds);
            config.ReplayCacheMinutes = ReadRegistryInt("ReplayCacheMinutes", config.ReplayCacheMinutes);
            config.RequireSignedRequests = ReadRegistryBool("RequireSignedRequests", config.RequireSignedRequests);
            config.MaxRequestsPerMinute = ReadRegistryInt("MaxRequestsPerMinute", config.MaxRequestsPerMinute);
            config.MaxConcurrentRequests = ReadRegistryInt("MaxConcurrentRequests", config.MaxConcurrentRequests);
            config.EnforceRevocationCheck = ReadRegistryBool("EnforceRevocationCheck", config.EnforceRevocationCheck);
            config.FailOpenOnRevocation = ReadRegistryBool("FailOpenOnRevocation", config.FailOpenOnRevocation);
            config.MaxBurstRequests = ReadRegistryInt("MaxBurstRequests", config.MaxBurstRequests);
            config.BurstWindowSeconds = ReadRegistryInt("BurstWindowSeconds", config.BurstWindowSeconds);
            config.RateLimitBackoffSeconds = ReadRegistryInt("RateLimitBackoffSeconds", config.RateLimitBackoffSeconds);

            return overrides;
        }

        private static string ReadRegistryValue(string name, string fallback)
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\DirectoryAnalyzer\Agent");
            var value = key?.GetValue(name) as string;
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static int ReadRegistryInt(string name, int fallback)
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\DirectoryAnalyzer\Agent");
            var raw = key?.GetValue(name);
            return raw is int typed ? typed : fallback;
        }

        private static bool ReadRegistryBool(string name, bool fallback)
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\DirectoryAnalyzer\Agent");
            var raw = key?.GetValue(name);
            return raw is int intValue ? intValue != 0 : fallback;
        }
    }
}
