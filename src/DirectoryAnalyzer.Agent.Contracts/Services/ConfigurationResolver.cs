using System;
using System.IO;

namespace DirectoryAnalyzer.Agent.Contracts.Services
{
    public enum ConfigurationSource
    {
        ProgramData,
        LegacyProgramData,
        BaseDirectory,
        DefaultCreated
    }

    public enum MigrationStatus
    {
        None,
        Migrated,
        LegacyIgnored,
        MigrationFailed,
        NotFound
    }

    public sealed class ConfigurationResolutionResult
    {
        public string SelectedPath { get; set; }
        public string ProgramDataPath { get; set; }
        public string LegacyProgramDataPath { get; set; }
        public string BaseDirectoryPath { get; set; }
        public string LogPath { get; set; }
        public ConfigurationSource Source { get; set; }
        public MigrationStatus MigrationStatus { get; set; }
        public string MigrationDetails { get; set; }

        public string[] PrecedenceOrder => new[]
        {
            "ProgramData",
            "LegacyProgramData",
            "BaseDirectory",
            "Default"
        };
    }

    public sealed class ConfigurationResolver
    {
        private readonly PathPolicy _policy;

        public ConfigurationResolver(PathPolicy policy)
        {
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        public ConfigurationResolutionResult ResolveAgentConfig()
        {
            return Resolve(
                _policy.ProgramDataAgentConfigPath,
                _policy.LegacyAgentConfigPath,
                _policy.BaseDirectoryAgentConfigPath,
                _policy.DefaultAgentLogPath);
        }

        public ConfigurationResolutionResult ResolveAnalyzerClientConfig()
        {
            return Resolve(
                _policy.ProgramDataAgentClientConfigPath,
                _policy.LegacyAgentClientConfigPath,
                _policy.BaseDirectoryAgentClientConfigPath,
                _policy.DefaultAnalyzerClientLogPath);
        }

        private static ConfigurationResolutionResult Resolve(
            string programDataPath,
            string legacyPath,
            string baseDirectoryPath,
            string defaultLogPath)
        {
            var result = new ConfigurationResolutionResult
            {
                ProgramDataPath = programDataPath,
                LegacyProgramDataPath = legacyPath,
                BaseDirectoryPath = baseDirectoryPath,
                LogPath = defaultLogPath,
                MigrationStatus = MigrationStatus.None
            };

            var programDataExists = File.Exists(programDataPath);
            var legacyExists = File.Exists(legacyPath);
            var baseExists = File.Exists(baseDirectoryPath);

            if (programDataExists)
            {
                result.SelectedPath = programDataPath;
                result.Source = ConfigurationSource.ProgramData;
                if (legacyExists)
                {
                    result.MigrationStatus = MigrationStatus.LegacyIgnored;
                    result.MigrationDetails = $"Legacy config ignored at {legacyPath}.";
                }

                return result;
            }

            if (legacyExists)
            {
                try
                {
                    var directory = Path.GetDirectoryName(programDataPath);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.Copy(legacyPath, programDataPath, false);
                    result.MigrationStatus = MigrationStatus.Migrated;
                    result.MigrationDetails = $"Legacy config copied from {legacyPath} to {programDataPath}.";
                    result.SelectedPath = programDataPath;
                    result.Source = ConfigurationSource.ProgramData;
                    return result;
                }
                catch (Exception ex)
                {
                    result.MigrationStatus = MigrationStatus.MigrationFailed;
                    result.MigrationDetails = $"Failed to copy legacy config from {legacyPath} to {programDataPath}. {ex.Message}";
                    result.SelectedPath = legacyPath;
                    result.Source = ConfigurationSource.LegacyProgramData;
                    return result;
                }
            }

            if (baseExists)
            {
                result.SelectedPath = baseDirectoryPath;
                result.Source = ConfigurationSource.BaseDirectory;
                return result;
            }

            result.SelectedPath = programDataPath;
            result.Source = ConfigurationSource.DefaultCreated;
            result.MigrationStatus = MigrationStatus.NotFound;
            result.MigrationDetails = $"No config found, expected {programDataPath} or {baseDirectoryPath}.";
            return result;
        }
    }
}
