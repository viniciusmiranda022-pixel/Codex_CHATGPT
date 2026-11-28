using System;
using System.Collections.Generic;

namespace DirectoryAnalyzer.Services
{
    /// <summary>
    /// Módulos cujas telas ainda enviam script PowerShell inline com fan-out remoto. O agente os recusa
    /// (ArbitraryScriptRejected / ModuleNotLocal), então a WPF avisa antes de enviar qualquer coisa.
    /// Migração decidida na issue #48: variante local por host (B.1) ou agente do ambiente com leitura delegada.
    /// </summary>
    public static class ModuleAvailability
    {
        public const string PendingMigrationCode = "ModulePendingMigration";

        private static readonly HashSet<string> PendingMigration = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
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

        public static IReadOnlyCollection<string> PendingMigrationModules => PendingMigration;

        /// <summary>Retorna o desfecho "indisponível" quando o módulo não pode ser enviado ao agente.</summary>
        public static bool TryGetUnavailable(string moduleName, out CollectionOutcome outcome)
        {
            outcome = null;
            if (string.IsNullOrWhiteSpace(moduleName) || !PendingMigration.Contains(moduleName))
            {
                return false;
            }

            outcome = CollectionOutcome.ModuleUnavailable(
                PendingMigrationCode,
                $"'{moduleName}' ainda consulta outros servidores por script e não é aceito pelo agente. " +
                "Aguardando migração para módulo local do catálogo (issue #48). Nada foi enviado ao agente.");
            return true;
        }
    }
}
