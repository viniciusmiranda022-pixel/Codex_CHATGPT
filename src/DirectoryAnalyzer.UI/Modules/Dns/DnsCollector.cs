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
        private readonly BrokerJobService _brokerJobService;
        private readonly ILogService _logService;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public DnsCollector(BrokerJobService brokerJobService, ILogService logService)
        {
            _brokerJobService = brokerJobService ?? throw new ArgumentNullException(nameof(brokerJobService));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        }

        public async Task<DnsReport> CollectAsync(CancellationToken cancellationToken, IProgress<string> progress)
        {
            progress?.Report("Conectando ao DNS...");
            _logService.Info("Iniciando coleta completa de DNS.");

            string scriptText = @"
                Import-Module DnsServer -ErrorAction SilentlyContinue
                if (-not (Get-Module DnsServer)) { throw 'Módulo DnsServer não encontrado.' }
                $pdc = (Get-ADDomainController -Discover -Service PrimaryDC).HostName

                $zonas = Get-DnsServerZone -ComputerName $pdc | Select-Object ZoneName, ZoneType, IsReverseLookupZone, DynamicUpdate
                $forwarders = Get-DnsServerForwarder -ComputerName $pdc | Select-Object @{N='IPAddress';E={($_.IPAddress | ForEach-Object { $_.IPAddressToString }) -join '; '}}, UseRootHint, EnableReordering, Timeout

                $allRecords = @()
                foreach($zone in $zonas){
                    $recordsInZone = Get-DnsServerResourceRecord -ZoneName $zone.ZoneName -ComputerName $pdc -ErrorAction SilentlyContinue
                    if($recordsInZone){
                        foreach ($record in $recordsInZone) {
                            $recordDataValue = 'N/A'
                            if ($record.RecordData) {
                                switch($record.RecordType){
                                    'A'     { $recordDataValue = '' + $record.RecordData.IPV4Address }
                                    'AAAA'  { $recordDataValue = '' + $record.RecordData.IPV6Address }
                                    'CNAME' { $recordDataValue = $record.RecordData.HostNameAlias }
                                    'MX'    { $recordDataValue = ""$($record.RecordData.MailExchange) (Pref: $($record.RecordData.Preference))"" }
                                    'NS'    { $recordDataValue = $record.RecordData.NameServer }
                                    'PTR'   { $recordDataValue = $record.RecordData.PtrDomainName }
                                    'SRV'   { $recordDataValue = ""$($record.RecordData.DomainName):$($record.RecordData.Port)"" }
                                    'SOA'   { $recordDataValue = ""Servidor Principal: $($record.RecordData.PrimaryServer)"" }
                                    default { $recordDataValue = ""Tipo não mapeado: $($record.RecordType)"" }
                                }
                            }
                            $allRecords += [PSCustomObject]@{
                                ZoneName   = $zone.ZoneName
                                HostName   = [string]$record.HostName
                                RecordType = [string]$record.RecordType
                                TimeToLive = [string]$record.TimeToLive
                                RecordData = $recordDataValue
                            }
                        }
                    }
                }

                [PSCustomObject]@{
                    ZonasJson = ($zonas | ConvertTo-Json -Depth 6)
                    RegistrosJson = ($allRecords | ConvertTo-Json -Depth 6)
                    EncaminhadoresJson = ($forwarders | ConvertTo-Json -Depth 6)
                }
            ";

            var moduleResult = await _brokerJobService.RunPowerShellScriptAsync(
                "DnsAnalyzer",
                scriptText,
                null,
                Environment.UserName,
                cancellationToken);
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
