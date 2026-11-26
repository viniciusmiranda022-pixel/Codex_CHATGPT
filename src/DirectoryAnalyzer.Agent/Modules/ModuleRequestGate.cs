using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DirectoryAnalyzer.Contracts;

namespace DirectoryAnalyzer.Agent.Modules
{
    /// <summary>Identidade do agente instalado, usada para validar o alvo e preencher o ModuleResult.</summary>
    public sealed class AgentIdentity
    {
        public AgentIdentity(string agentId, string host, string domain)
        {
            if (string.IsNullOrWhiteSpace(agentId))
            {
                throw new ArgumentException("AgentId é obrigatório.", nameof(agentId));
            }

            AgentId = agentId;
            Host = host ?? string.Empty;
            Domain = domain ?? string.Empty;
        }

        public string AgentId { get; }
        public string Host { get; }
        public string Domain { get; }
    }

    public static class GateErrorCodes
    {
        public const string InvalidRequest = "InvalidRequest";
        public const string UnsupportedContract = "UnsupportedContract";
        public const string TargetMismatch = "TargetMismatch";
        public const string InvalidModuleName = "InvalidModuleName";
        public const string ArbitraryScriptRejected = "ArbitraryScriptRejected";
        public const string ModuleNotLocal = "ModuleNotLocal";
        public const string UnknownModule = "UnknownModule";
        public const string UnknownParameter = "UnknownParameter";
        public const string InvalidParameter = "InvalidParameter";
        public const string MissingParameter = "MissingParameter";
    }

    public sealed class GateDecision
    {
        private GateDecision(bool accepted, ModuleDefinition module, IReadOnlyDictionary<string, string> parameters, string errorCode, string errorMessage)
        {
            Accepted = accepted;
            Module = module;
            Parameters = parameters;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
        }

        public bool Accepted { get; }
        public ModuleDefinition Module { get; }

        /// <summary>Parâmetros normalizados (nome canônico do catálogo). Somente quando aceito.</summary>
        public IReadOnlyDictionary<string, string> Parameters { get; }

        public string ErrorCode { get; }
        public string ErrorMessage { get; }

        internal static GateDecision Accept(ModuleDefinition module, IReadOnlyDictionary<string, string> parameters)
            => new GateDecision(true, module, parameters, null, null);

        internal static GateDecision Reject(string code, string message)
            => new GateDecision(false, null, null, code, message);
    }

    /// <summary>
    /// Validação feita pelo agente antes de qualquer execução: contrato, alvo, módulo do catálogo
    /// e parâmetros declarados. Nada do pedido é executado se a decisão for de recusa.
    /// </summary>
    public sealed class ModuleRequestGate
    {
        public const string SupportedContractVersion = "1.0";
        private const int MaxParameterValueLength = 512;
        private static readonly Regex ModuleNamePattern = new Regex("^[A-Za-z][A-Za-z0-9]{0,63}$", RegexOptions.CultureInvariant);
        private static readonly Regex ParameterNamePattern = new Regex("^[A-Za-z][A-Za-z0-9]{0,63}$", RegexOptions.CultureInvariant);
        private static readonly Regex LdapFilterCharset = new Regex(@"^[a-zA-Z0-9=\*\(\)\&\|\!\-\._@\s\\:;,\+/]+$", RegexOptions.CultureInvariant);

        private readonly ModuleCatalog _catalog;
        private readonly AgentIdentity _identity;

        public ModuleRequestGate(ModuleCatalog catalog, AgentIdentity identity)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        }

        public GateDecision Evaluate(JobRequest request)
        {
            if (request == null)
            {
                return GateDecision.Reject(GateErrorCodes.InvalidRequest, "Pedido vazio.");
            }

            if (!string.Equals(request.ContractVersion, SupportedContractVersion, StringComparison.Ordinal))
            {
                return GateDecision.Reject(GateErrorCodes.UnsupportedContract,
                    $"Versão de contrato não suportada pelo agente (esperado {SupportedContractVersion}).");
            }

            var targets = request.TargetAgentIds ?? new List<string>();
            if (!targets.Any(id => string.Equals(id, _identity.AgentId, StringComparison.OrdinalIgnoreCase)))
            {
                return GateDecision.Reject(GateErrorCodes.TargetMismatch,
                    "O pedido não designa este agente como alvo.");
            }

            var moduleName = request.ModuleName;
            if (string.IsNullOrWhiteSpace(moduleName) || !ModuleNamePattern.IsMatch(moduleName))
            {
                return GateDecision.Reject(GateErrorCodes.InvalidModuleName, "Nome de módulo inválido.");
            }

            if (ModuleCatalog.IsForbiddenAction(moduleName))
            {
                return GateDecision.Reject(GateErrorCodes.ArbitraryScriptRejected,
                    "O agente não executa scripts enviados pelo solicitante; peça um módulo do catálogo pelo nome.");
            }

            if (ModuleCatalog.IsPendingNonLocalModule(moduleName))
            {
                return GateDecision.Reject(GateErrorCodes.ModuleNotLocal,
                    $"O módulo '{moduleName}' consulta outros servidores e não está habilitado neste agente.");
            }

            if (!_catalog.TryGet(moduleName, out var module))
            {
                return GateDecision.Reject(GateErrorCodes.UnknownModule,
                    $"O módulo '{moduleName}' não está instalado neste agente.");
            }

            var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in request.Parameters ?? new Dictionary<string, string>())
            {
                var key = pair.Key ?? string.Empty;
                if (!ParameterNamePattern.IsMatch(key) || !module.Parameters.TryGetValue(key, out var spec))
                {
                    return GateDecision.Reject(GateErrorCodes.UnknownParameter,
                        $"Parâmetro '{Describe(key)}' não é aceito pelo módulo '{module.Name}'.");
                }

                if (normalized.ContainsKey(spec.Name))
                {
                    return GateDecision.Reject(GateErrorCodes.InvalidParameter, $"Parâmetro '{spec.Name}' duplicado.");
                }

                if (!TryNormalize(spec, pair.Value, out var value, out var error))
                {
                    return GateDecision.Reject(GateErrorCodes.InvalidParameter, error);
                }

                normalized[spec.Name] = value;
            }

            foreach (var spec in module.Parameters.Values.Where(p => p.Required))
            {
                if (!normalized.ContainsKey(spec.Name))
                {
                    return GateDecision.Reject(GateErrorCodes.MissingParameter, $"Parâmetro obrigatório '{spec.Name}' ausente.");
                }
            }

            return GateDecision.Accept(module, normalized);
        }

        private static bool TryNormalize(ModuleParameterSpec spec, string raw, out string value, out string error)
        {
            value = null;
            error = null;
            if (raw == null || raw.Length > MaxParameterValueLength)
            {
                error = $"Parâmetro '{spec.Name}' ausente ou acima de {MaxParameterValueLength} caracteres.";
                return false;
            }

            switch (spec.Kind)
            {
                case ModuleParameterKind.Boolean:
                    if (!bool.TryParse(raw.Trim(), out var flag))
                    {
                        error = $"Parâmetro '{spec.Name}' deve ser true ou false.";
                        return false;
                    }

                    value = flag ? "true" : "false";
                    return true;

                case ModuleParameterKind.LdapFilter:
                    var trimmed = raw.Trim();
                    if (trimmed.Length == 0
                        || !trimmed.StartsWith("(", StringComparison.Ordinal)
                        || !trimmed.EndsWith(")", StringComparison.Ordinal)
                        || !LdapFilterCharset.IsMatch(trimmed)
                        || !HasBalancedParentheses(trimmed))
                    {
                        error = $"Parâmetro '{spec.Name}' não é um filtro LDAP aceito.";
                        return false;
                    }

                    value = trimmed;
                    return true;

                default:
                    error = $"Tipo do parâmetro '{spec.Name}' não suportado.";
                    return false;
            }
        }

        private static bool HasBalancedParentheses(string filter)
        {
            var depth = 0;
            foreach (var ch in filter)
            {
                if (ch == '(')
                {
                    depth++;
                }
                else if (ch == ')' && --depth < 0)
                {
                    return false;
                }
            }

            return depth == 0;
        }

        private static string Describe(string key)
        {
            var safe = new string((key ?? string.Empty).Where(char.IsLetterOrDigit).Take(64).ToArray());
            return safe.Length == 0 ? "?" : safe;
        }
    }
}
