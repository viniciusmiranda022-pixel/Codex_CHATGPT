using System;
using System.Collections.Generic;

namespace DirectoryAnalyzer.Agent.Modules
{
    /// <summary>
    /// Escopo que o próprio agente impõe ao módulo. O solicitante nunca informa alvo.
    /// </summary>
    public enum ModuleScope
    {
        /// <summary>Leitura LDAP do domínio configurado no agente (AgentConfig.Domain ou RootDSE local).</summary>
        AgentDomainDirectory = 0,

        /// <summary>Leitura de dados do próprio host em que o agente está instalado.</summary>
        LocalHost = 1
    }

    /// <summary>
    /// Tipos de parâmetro aceitos no contrato remoto. Não há tipo de texto livre nem filtro LDAP:
    /// a expressão de consulta é fixa no agente (decisão de revisão da #49).
    /// </summary>
    public enum ModuleParameterKind
    {
        Boolean = 0
    }

    public sealed class ModuleParameterSpec
    {
        public ModuleParameterSpec(string name, ModuleParameterKind kind, bool required = false)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Kind = kind;
            Required = required;
        }

        public string Name { get; }
        public ModuleParameterKind Kind { get; }
        public bool Required { get; }
    }

    public sealed class ModuleDefinition
    {
        public ModuleDefinition(string name, ModuleScope scope, params ModuleParameterSpec[] parameters)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Scope = scope;
            var map = new Dictionary<string, ModuleParameterSpec>(StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in parameters ?? Array.Empty<ModuleParameterSpec>())
            {
                map.Add(parameter.Name, parameter);
            }

            Parameters = map;
        }

        public string Name { get; }
        public ModuleScope Scope { get; }

        /// <summary>Todos os módulos do catálogo são de leitura; não existe módulo de escrita.</summary>
        public bool ReadOnly => true;

        public IReadOnlyDictionary<string, ModuleParameterSpec> Parameters { get; }
    }

    /// <summary>
    /// Catálogo de módulos que o agente aceita executar. É definido no binário instalado,
    /// não pela UI nem pelo Broker.
    /// </summary>
    public sealed class ModuleCatalog
    {
        /// <summary>
        /// Módulos existentes no repositório que fazem fan-out remoto (Get-ADComputer + Invoke-Command /
        /// -ComputerName) ou consultam outro servidor. Permanecem fora do catálogo habilitado até a decisão
        /// registrada na issue #48 (variante local por host vs. fan-out restrito).
        /// </summary>
        private static readonly HashSet<string> PendingNonLocal = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ScheduledTasksAnalyzer",
            "SmbSharesAnalyzer",
            "InstalledServicesAnalyzer",
            "LocalProfilesAnalyzer",
            "LocalSecurityPolicyAnalyzer",
            "IisAppPoolsAnalyzer",
            "ProxyAddressAnalyzer",
            "TrustsAnalyzer",
            "GpoAnalyzer",
            "DnsAnalyzer"
        };

        /// <summary>Ações que recebem código do solicitante e nunca são aceitas.</summary>
        private static readonly HashSet<string> Forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RunPowerShellScript"
        };

        public static IReadOnlyCollection<string> PendingNonLocalModules => PendingNonLocal;

        public static IReadOnlyCollection<string> ForbiddenActions => Forbidden;

        public static bool IsForbiddenAction(string name) => name != null && Forbidden.Contains(name);

        public static bool IsPendingNonLocalModule(string name) => name != null && PendingNonLocal.Contains(name);

        private readonly Dictionary<string, ModuleDefinition> _modules;

        public ModuleCatalog(IEnumerable<ModuleDefinition> modules)
        {
            _modules = new Dictionary<string, ModuleDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var module in modules ?? throw new ArgumentNullException(nameof(modules)))
            {
                if (IsForbiddenAction(module.Name))
                {
                    throw new ArgumentException($"'{module.Name}' não pode fazer parte do catálogo.", nameof(modules));
                }

                _modules.Add(module.Name, module);
            }
        }

        public IReadOnlyCollection<string> ModuleNames => _modules.Keys;

        public bool TryGet(string moduleName, out ModuleDefinition module)
        {
            module = null;
            return !string.IsNullOrEmpty(moduleName) && _modules.TryGetValue(moduleName, out module);
        }

        /// <summary>
        /// Catálogo padrão: somente leitura LDAP do domínio do próprio agente. As bases consultadas por
        /// módulo estão em <see cref="DirectorySearchBases"/> (GetDnsZones: apenas DomainDnsZones).
        /// </summary>
        public static ModuleCatalog CreateDefault()
        {
            return new ModuleCatalog(new[]
            {
                new ModuleDefinition("GetUsers", ModuleScope.AgentDomainDirectory,
                    new ModuleParameterSpec("IncludeDisabled", ModuleParameterKind.Boolean)),
                new ModuleDefinition("GetGroups", ModuleScope.AgentDomainDirectory),
                new ModuleDefinition("GetComputers", ModuleScope.AgentDomainDirectory),
                new ModuleDefinition("GetGpos", ModuleScope.AgentDomainDirectory),
                new ModuleDefinition("GetDnsZones", ModuleScope.AgentDomainDirectory)
            });
        }
    }
}
