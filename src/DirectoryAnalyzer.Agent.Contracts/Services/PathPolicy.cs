using System;
using System.IO;

namespace DirectoryAnalyzer.Agent.Contracts.Services
{
    public sealed class PathPolicy
    {
        public PathPolicy(string programDataRoot = null, string baseDirectory = null)
        {
            ProgramDataRoot = string.IsNullOrWhiteSpace(programDataRoot)
                ? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
                : programDataRoot;
            BaseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
                ? AppDomain.CurrentDomain.BaseDirectory
                : baseDirectory;
        }

        public string ProgramDataRoot { get; }
        public string BaseDirectory { get; }

        public string ProgramDataDirectoryName => "DirectoryAnalyzerAgent";
        public string LegacyProgramDataDirectoryName => "DirectoryAnalyzer";

        public string AgentConfigFileName => "agentsettings.json";
        public string AgentClientConfigFileName => "agentclientsettings.json";

        public string AgentLogFileName => "agent.log";
        public string AnalyzerClientLogFileName => "analyzerclient.log";

        public string ProgramDataAgentConfigPath => Path.Combine(ProgramDataRoot, ProgramDataDirectoryName, AgentConfigFileName);
        public string LegacyAgentConfigPath => Path.Combine(ProgramDataRoot, LegacyProgramDataDirectoryName, AgentConfigFileName);
        public string BaseDirectoryAgentConfigPath => Path.Combine(BaseDirectory, AgentConfigFileName);

        public string ProgramDataAgentClientConfigPath => Path.Combine(ProgramDataRoot, ProgramDataDirectoryName, AgentClientConfigFileName);
        public string LegacyAgentClientConfigPath => Path.Combine(ProgramDataRoot, LegacyProgramDataDirectoryName, AgentClientConfigFileName);
        public string BaseDirectoryAgentClientConfigPath => Path.Combine(BaseDirectory, AgentClientConfigFileName);

        public string DefaultAgentLogPath => Path.Combine(ProgramDataRoot, ProgramDataDirectoryName, "Logs", AgentLogFileName);
        public string DefaultAnalyzerClientLogPath => Path.Combine(ProgramDataRoot, ProgramDataDirectoryName, "Logs", AnalyzerClientLogFileName);
    }
}
