using System;
using System.Collections.Generic;

namespace DirectoryAnalyzer.Agent.Modules
{
    /// <summary>Valores lidos do RootDSE do domínio do agente. Nunca vêm do solicitante.</summary>
    public sealed class RootDseSnapshot
    {
        public string DefaultNamingContext { get; set; }
        public string DomainDnsZones { get; set; }
        public string ForestDnsZones { get; set; }
        public string RootDomainNamingContext { get; set; }
    }

    /// <summary>
    /// Bases LDAP que cada módulo do catálogo pode consultar. Todas ficam dentro do domínio do próprio
    /// agente (sufixo = defaultNamingContext). Partições de floresta (ForestDnsZones, Configuration,
    /// domínio raiz) nunca são selecionadas neste incremento.
    /// </summary>
    public static class DirectorySearchBases
    {
        public static IReadOnlyList<string> For(string moduleName, RootDseSnapshot rootDse)
        {
            var domainNc = rootDse?.DefaultNamingContext?.Trim();
            if (string.IsNullOrEmpty(domainNc))
            {
                throw new InvalidOperationException("defaultNamingContext indisponível; o agente não define o escopo da consulta.");
            }

            switch (moduleName)
            {
                case "GetUsers":
                case "GetGroups":
                case "GetComputers":
                    return new[] { domainNc };

                case "GetGpos":
                    return new[] { $"CN=Policies,CN=System,{domainNc}" };

                case "GetDnsZones":
                    // Somente a partição DNS do domínio. O valor anunciado pelo RootDSE só é usado se estiver
                    // dentro do domínio; ForestDnsZones é ignorado de propósito.
                    var expected = $"DC=DomainDnsZones,{domainNc}";
                    var announced = rootDse.DomainDnsZones?.Trim();
                    var partition = string.Equals(announced, expected, StringComparison.OrdinalIgnoreCase) ? announced : expected;
                    return new[] { $"CN=MicrosoftDNS,{partition}" };

                default:
                    throw new ArgumentException($"Módulo '{moduleName}' não tem base de consulta definida.", nameof(moduleName));
            }
        }

        public static bool IsWithinDomain(string distinguishedName, RootDseSnapshot rootDse)
        {
            var domainNc = rootDse?.DefaultNamingContext?.Trim();
            if (string.IsNullOrEmpty(domainNc) || string.IsNullOrEmpty(distinguishedName))
            {
                return false;
            }

            return string.Equals(distinguishedName, domainNc, StringComparison.OrdinalIgnoreCase)
                || distinguishedName.EndsWith("," + domainNc, StringComparison.OrdinalIgnoreCase);
        }
    }
}

namespace DirectoryAnalyzer.Agent.Modules
{
    /// <summary>
    /// Escopo efetivo de uma coleta LDAP, derivado do RootDSE lido pelo próprio agente.
    /// Política de referências: <see cref="ReferralPolicy"/> = None. O agente não segue referências LDAP
    /// (subordinadas para domínios filhos nem externas para outros domínios/florestas); o servidor
    /// devolve só objetos do naming context do domínio consultado. Como defesa adicional, o
    /// AgentJobProcessor descarta qualquer linha cujo DN esteja fora de <see cref="DomainNamingContext"/>.
    /// </summary>
    public sealed class DirectoryQueryScope
    {
        /// <summary>Equivale a System.DirectoryServices.ReferralChasingOption.None.</summary>
        public const string ReferralPolicy = "None";

        private readonly RootDseSnapshot _rootDse;

        private DirectoryQueryScope(RootDseSnapshot rootDse, string domainNamingContext)
        {
            _rootDse = rootDse;
            DomainNamingContext = domainNamingContext;
            DomainDnsName = ToDnsName(domainNamingContext);
        }

        public string DomainNamingContext { get; }

        /// <summary>Nome DNS do domínio efetivamente consultado (ex.: filho.origem.example).</summary>
        public string DomainDnsName { get; }

        public static DirectoryQueryScope From(RootDseSnapshot rootDse)
        {
            var domainNc = rootDse?.DefaultNamingContext?.Trim();
            if (string.IsNullOrEmpty(domainNc) || string.IsNullOrEmpty(ToDnsName(domainNc)))
            {
                throw new InvalidOperationException("defaultNamingContext indisponível; o agente não define o escopo da consulta.");
            }

            return new DirectoryQueryScope(rootDse, domainNc);
        }

        public IReadOnlyList<string> BasesFor(string moduleName) => DirectorySearchBases.For(moduleName, _rootDse);

        /// <summary>
        /// O DN pertence a uma das bases do módulo e não atravessa a fronteira de um domínio filho.
        /// Sob a base do domínio (DC=...), um RDN DC= logo antes da base indica outro naming context
        /// (domínio filho ou partição de aplicação) e é recusado. Sob bases CN= (Policies, MicrosoftDNS)
        /// não há domínio aninhado, então qualquer descendente é aceito (ex.: zonas DNS "DC=zona,CN=MicrosoftDNS,...").
        /// </summary>
        public bool Contains(string moduleName, string distinguishedName)
        {
            if (string.IsNullOrWhiteSpace(distinguishedName))
            {
                return false;
            }

            var dn = distinguishedName.Trim();
            foreach (var searchBase in BasesFor(moduleName))
            {
                if (string.Equals(dn, searchBase, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!dn.EndsWith("," + searchBase, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!searchBase.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var relative = dn.Substring(0, dn.Length - searchBase.Length - 1);
                var lastRdn = LastRdn(relative);
                if (!lastRdn.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string LastRdn(string relativeDn)
        {
            // Último separador não escapado ("\," faz parte do valor).
            for (var i = relativeDn.Length - 1; i >= 0; i--)
            {
                if (relativeDn[i] != ',')
                {
                    continue;
                }

                var backslashes = 0;
                for (var j = i - 1; j >= 0 && relativeDn[j] == '\\'; j--)
                {
                    backslashes++;
                }

                if (backslashes % 2 == 0)
                {
                    return relativeDn.Substring(i + 1).Trim();
                }
            }

            return relativeDn.Trim();
        }

        public static string ToDnsName(string namingContext)
        {
            if (string.IsNullOrWhiteSpace(namingContext))
            {
                return null;
            }

            var labels = new List<string>();
            foreach (var part in namingContext.Split(','))
            {
                var trimmed = part.Trim();
                if (!trimmed.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
                {
                    // Naming context de domínio só tem componentes DC=.
                    return null;
                }

                var label = trimmed.Substring(3).Trim();
                if (label.Length == 0)
                {
                    return null;
                }

                labels.Add(label.ToLowerInvariant());
            }

            return string.Join(".", labels);
        }
    }
}
