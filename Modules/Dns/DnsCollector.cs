using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DirectoryAnalyzer.Models;
using DirectoryAnalyzer.Services;
using System.Management.Automation;

namespace DirectoryAnalyzer.Modules.Dns
{
    public class DnsCollector : ICollector<DnsReport>
    {
        private readonly PowerShellService _powerShellService;
        private readonly ILogService _logService;

        public DnsCollector(PowerShellService powerShellService, ILogService logService)
        {
            _powerShellService = powerShellService ?? throw new ArgumentNullException(nameof(powerShellService));
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
                    Zonas = $zonas
                    Registros = $allRecords
                    Encaminhadores = $forwarders
                }
            ";

            var results = await _powerShellService.ExecuteScriptAsync(scriptText, cancellationToken);
            var report = new DnsReport();

            if (results.FirstOrDefault() is IDictionary<string, object> data)
            {
                progress?.Report("Processando zonas...");
                report.Zones.AddRange(MapZoneResults(data.TryGetValue("Zonas", out var zonas) ? zonas as IEnumerable : null));

                progress?.Report("Processando registros...");
                report.Records.AddRange(MapRecordResults(data.TryGetValue("Registros", out var registros) ? registros as IEnumerable : null));

                progress?.Report("Processando encaminhadores...");
                report.Forwarders.AddRange(MapForwarderResults(data.TryGetValue("Encaminhadores", out var forwarders) ? forwarders as IEnumerable : null));
            }

            _logService.Info($"Coleta finalizada. {report.Zones.Count} zonas, {report.Records.Count} registros, {report.Forwarders.Count} encaminhadores.");
            progress?.Report("Coleta concluída.");
            return report;
        }

        private static IEnumerable<DnsZoneResult> MapZoneResults(IEnumerable items)
        {
            if (items == null)
            {
                yield break;
            }

            foreach (var item in items)
            {
                if (item is PSObject pso)
                {
                    yield return new DnsZoneResult
                    {
                        ZoneName = Convert.ToString(pso.Properties["ZoneName"]?.Value),
                        ZoneType = Convert.ToString(pso.Properties["ZoneType"]?.Value),
                        IsReverseLookupZone = Convert.ToString(pso.Properties["IsReverseLookupZone"]?.Value),
                        DynamicUpdate = Convert.ToString(pso.Properties["DynamicUpdate"]?.Value)
                    };
                }
            }
        }

        private static IEnumerable<DnsRecordResult> MapRecordResults(IEnumerable items)
        {
            if (items == null)
            {
                yield break;
            }

            foreach (var item in items)
            {
                if (item is PSObject pso)
                {
                    yield return new DnsRecordResult
                    {
                        ZoneName = Convert.ToString(pso.Properties["ZoneName"]?.Value),
                        HostName = Convert.ToString(pso.Properties["HostName"]?.Value),
                        RecordType = Convert.ToString(pso.Properties["RecordType"]?.Value),
                        TimeToLive = Convert.ToString(pso.Properties["TimeToLive"]?.Value),
                        RecordData = Convert.ToString(pso.Properties["RecordData"]?.Value)
                    };
                }
            }
        }

        private static IEnumerable<DnsForwarderResult> MapForwarderResults(IEnumerable items)
        {
            if (items == null)
            {
                yield break;
            }

            foreach (var item in items)
            {
                if (item is PSObject pso)
                {
                    yield return new DnsForwarderResult
                    {
                        IPAddress = Convert.ToString(pso.Properties["IPAddress"]?.Value),
                        UseRootHint = Convert.ToString(pso.Properties["UseRootHint"]?.Value),
                        EnableReordering = Convert.ToString(pso.Properties["EnableReordering"]?.Value),
                        Timeout = Convert.ToString(pso.Properties["Timeout"]?.Value)
                    };
                }
            }
        }
    }
}
