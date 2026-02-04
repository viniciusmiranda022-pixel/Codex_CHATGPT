using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace DirectoryAnalyzer.Services
{
    [DataContract]
    public sealed class AgentEndpoint
    {
        [DataMember(Order = 1)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [DataMember(Order = 2)]
        public string Name { get; set; } = "Default Agent";

        [DataMember(Order = 3)]
        public string Endpoint { get; set; } = "https://localhost:8443/agent/";

        [DataMember(Order = 4)]
        public string[] AllowedThumbprints { get; set; } = Array.Empty<string>();

        public string AllowedThumbprintsDisplay
        {
            get => string.Join(";", AllowedThumbprints ?? Array.Empty<string>());
            set => AllowedThumbprints = string.IsNullOrWhiteSpace(value)
                ? Array.Empty<string>()
                : value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }

    [DataContract]
    public sealed class AgentModeSettings
    {
        [DataMember(Order = 1)]
        public string SelectedAgentId { get; set; }

        [DataMember(Order = 2)]
        public string ClientCertThumbprint { get; set; } = string.Empty;

        [DataMember(Order = 3)]
        public int RequestTimeoutSeconds { get; set; } = 30;

        [DataMember(Order = 4)]
        public int MaxRetries { get; set; } = 2;

        [DataMember(Order = 5)]
        public List<AgentEndpoint> Agents { get; set; } = new List<AgentEndpoint> { new AgentEndpoint() };
    }

    public static class AgentSettingsStore
    {
        public static AgentModeSettings Load(string path)
        {
            if (!File.Exists(path))
            {
                var defaults = new AgentModeSettings();
                defaults.SelectedAgentId = defaults.Agents.FirstOrDefault()?.Id;
                Save(path, defaults);
                return defaults;
            }

            using var stream = File.OpenRead(path);
            var serializer = new DataContractJsonSerializer(typeof(AgentModeSettings));
            var settings = serializer.ReadObject(stream) as AgentModeSettings;
            if (settings == null)
            {
                throw new InvalidOperationException("Invalid agent settings.");
            }

            if (settings.Agents == null || settings.Agents.Count == 0)
            {
                settings.Agents = new List<AgentEndpoint> { new AgentEndpoint() };
            }

            if (string.IsNullOrWhiteSpace(settings.SelectedAgentId))
            {
                settings.SelectedAgentId = settings.Agents.First().Id;
            }

            return settings;
        }

        public static void Save(string path, AgentModeSettings settings)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = File.Create(path);
            var serializer = new DataContractJsonSerializer(typeof(AgentModeSettings));
            serializer.WriteObject(stream, settings);
        }

        public static string ResolveSettingsPath(string fileName)
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var sharedPath = Path.Combine(programData, "DirectoryAnalyzerAgent", fileName);
            if (File.Exists(sharedPath))
            {
                return sharedPath;
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
        }
    }
}
