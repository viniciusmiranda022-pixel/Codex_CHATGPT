using System;
using System.Collections.Generic;
using System.Linq;
using DirectoryAnalyzer.Agent.Contracts.Services;

namespace DirectoryAnalyzer.AnalyzerClient
{
    public static class AnalyzerConfigValidator
    {
        public static IReadOnlyList<string> Validate(AnalyzerConfig config)
        {
            var errors = new List<string>();
            if (config == null)
            {
                errors.Add("Config nula.");
                return errors;
            }

            if (string.IsNullOrWhiteSpace(config.AgentEndpoint))
            {
                errors.Add("AgentEndpoint é obrigatório.");
            }
            else if (!Uri.TryCreate(config.AgentEndpoint, UriKind.Absolute, out var uri))
            {
                errors.Add("AgentEndpoint inválido.");
            }
            else if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("AgentEndpoint deve usar https.");
            }

            if (string.IsNullOrWhiteSpace(config.ClientCertThumbprint))
            {
                errors.Add("ClientCertThumbprint é obrigatório.");
            }
            else if (!ThumbprintValidator.IsValid(config.ClientCertThumbprint))
            {
                errors.Add("ClientCertThumbprint inválido.");
            }

            var allowed = config.AllowedAgentThumbprints ?? Array.Empty<string>();
            if (allowed.Any(tp => !ThumbprintValidator.IsValid(tp)))
            {
                errors.Add("AllowedAgentThumbprints contém thumbprint inválido.");
            }

            if (config.RequestTimeoutSeconds <= 0)
            {
                errors.Add("RequestTimeoutSeconds deve ser maior que zero.");
            }

            return errors;
        }
    }
}
