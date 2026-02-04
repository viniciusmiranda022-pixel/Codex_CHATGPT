using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

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
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "DirectoryAnalyzerAgent",
                "Logs",
                "agent.log");
        }
    }

    public static class ConfigLoader
    {
        public static AgentConfig Load(string path)
        {
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                var serializer = new DataContractJsonSerializer(typeof(AgentConfig));
                var config = serializer.ReadObject(stream) as AgentConfig;
                if (config == null)
                {
                    throw new InvalidOperationException("Invalid agent configuration.");
                }

                ApplyRegistryOverrides(config);
                return config;
            }

            return CreateFromRegistry(path);
        }

        private static AgentConfig CreateFromRegistry(string path)
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

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppDomain.CurrentDomain.BaseDirectory);
            using var stream = File.Create(path);
            var serializer = new DataContractJsonSerializer(typeof(AgentConfig));
            serializer.WriteObject(stream, config);
            return config;
        }

        private static void ApplyRegistryOverrides(AgentConfig config)
        {
            var bindPrefix = ReadRegistryValue("BindPrefix", null);
            if (!string.IsNullOrWhiteSpace(bindPrefix))
            {
                config.BindPrefix = bindPrefix;
            }

            var certThumbprint = ReadRegistryValue("CertThumbprint", null);
            if (!string.IsNullOrWhiteSpace(certThumbprint))
            {
                config.CertThumbprint = certThumbprint;
            }

            var analyzerThumbprints = ReadRegistryValue("AnalyzerClientThumbprints", null);
            if (!string.IsNullOrWhiteSpace(analyzerThumbprints))
            {
                config.AnalyzerClientThumbprints = analyzerThumbprints
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            }

            var logPath = ReadRegistryValue("LogPath", null);
            if (!string.IsNullOrWhiteSpace(logPath))
            {
                config.LogPath = logPath;
            }

            var domain = ReadRegistryValue("Domain", null);
            if (domain != null)
            {
                config.Domain = domain;
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
