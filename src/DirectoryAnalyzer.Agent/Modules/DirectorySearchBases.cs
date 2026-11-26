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
