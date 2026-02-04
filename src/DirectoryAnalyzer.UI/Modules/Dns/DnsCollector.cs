using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DirectoryAnalyzer.Models;
using DirectoryAnalyzer.Services;

namespace DirectoryAnalyzer.Modules.Dns
{
    public class DnsCollector : ICollector<DnsReport>
    {
        private readonly ModuleCollectionService _collectionService;
        private readonly ILogService _logService;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public DnsCollector(BrokerJobService brokerJobService, ILogService logService)
        {
            _collectionService = new ModuleCollectionService(brokerJobService ?? throw new ArgumentNullException(nameof(brokerJobService)));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        }

        public async Task<DnsReport> CollectAsync(CancellationToken cancellationToken, IProgress<string> progress)
        {
            progress?.Report("Conectando ao DNS...");
            _logService.Info("Iniciando coleta completa de DNS.");

            var moduleResult = await _collectionService.RunDnsAsync(Environment.UserName, cancellationToken);
            var report = new DnsReport();

            if (moduleResult?.Items?.Count > 0)
            {
                progress?.Report("Processando zonas...");
                report.Zones.AddRange(ParseList<DnsZoneResult>(moduleResult.Items[0].Columns, "ZonasJson"));

                progress?.Report("Processando registros...");
                report.Records.AddRange(ParseList<DnsRecordResult>(moduleResult.Items[0].Columns, "RegistrosJson"));

                progress?.Report("Processando encaminhadores...");
                report.Forwarders.AddRange(ParseList<DnsForwarderResult>(moduleResult.Items[0].Columns, "EncaminhadoresJson"));
            }

            _logService.Info($"Coleta finalizada. {report.Zones.Count} zonas, {report.Records.Count} registros, {report.Forwarders.Count} encaminhadores.");
            progress?.Report("Coleta concluída.");
            return report;
        }

        private static IEnumerable<T> ParseList<T>(IDictionary<string, string> columns, string key)
        {
            if (columns == null || !columns.TryGetValue(key, out var json) || string.IsNullOrWhiteSpace(json))
            {
                return Enumerable.Empty<T>();
            }

            return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? Enumerable.Empty<T>();
        }
    }
}
