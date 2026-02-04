using System;
using System.Collections.Generic;
using System.Linq;

namespace DirectoryAnalyzer.Agent.Contracts.Services
{
    public static class AgentConfigValidator
    {
        public static IReadOnlyList<string> Validate(AgentConfig config)
        {
            var errors = new List<string>();
            if (config == null)
            {
                errors.Add("Config nula.");
                return errors;
            }

            if (string.IsNullOrWhiteSpace(config.BindPrefix))
            {
                errors.Add("BindPrefix é obrigatório.");
            }
            else if (!config.BindPrefix.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("BindPrefix deve iniciar com https://");
            }

            if (string.IsNullOrWhiteSpace(config.CertThumbprint))
            {
                errors.Add("CertThumbprint é obrigatório.");
            }
            else if (!ThumbprintValidator.IsValid(config.CertThumbprint))
            {
                errors.Add("CertThumbprint inválido.");
            }

            var clientThumbprints = config.AnalyzerClientThumbprints ?? Array.Empty<string>();
            if (clientThumbprints.Length == 0)
            {
                errors.Add("AnalyzerClientThumbprints é obrigatório.");
            }
            else if (clientThumbprints.Any(tp => !ThumbprintValidator.IsValid(tp)))
            {
                errors.Add("AnalyzerClientThumbprints contém thumbprint inválido.");
            }

            if (string.IsNullOrWhiteSpace(config.LogPath))
            {
                errors.Add("LogPath é obrigatório.");
            }

            return errors;
        }
    }
}
