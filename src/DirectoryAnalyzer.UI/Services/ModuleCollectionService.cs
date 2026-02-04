using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DirectoryAnalyzer.Contracts;

namespace DirectoryAnalyzer.Services
{
    public sealed class ModuleCollectionService
    {
        private readonly BrokerJobService _brokerJobService;

        public ModuleCollectionService(BrokerJobService brokerJobService)
        {
            _brokerJobService = brokerJobService ?? throw new ArgumentNullException(nameof(brokerJobService));
        }

        public Task<ModuleResult> RunScheduledTasksAsync(string attributeName, string attributeValue, string requestedBy, CancellationToken token)
        {
            return RunModuleWithScopeAsync("ScheduledTasksAnalyzer", attributeName, attributeValue, requestedBy, token);
        }

        public Task<ModuleResult> RunSmbSharesAsync(string attributeName, string attributeValue, string requestedBy, CancellationToken token)
        {
            return RunModuleWithScopeAsync("SmbSharesAnalyzer", attributeName, attributeValue, requestedBy, token);
        }

        public Task<ModuleResult> RunInstalledServicesAsync(string attributeName, string attributeValue, string requestedBy, CancellationToken token)
        {
            return RunModuleWithScopeAsync("InstalledServicesAnalyzer", attributeName, attributeValue, requestedBy, token);
        }

        public Task<ModuleResult> RunLocalProfilesAsync(string attributeName, string attributeValue, string requestedBy, CancellationToken token)
        {
            return RunModuleWithScopeAsync("LocalProfilesAnalyzer", attributeName, attributeValue, requestedBy, token);
        }

        public Task<ModuleResult> RunLocalSecurityPolicyAsync(string attributeName, string attributeValue, string requestedBy, CancellationToken token)
        {
            return RunModuleWithScopeAsync("LocalSecurityPolicyAnalyzer", attributeName, attributeValue, requestedBy, token);
        }

        public Task<ModuleResult> RunIisAppPoolsAsync(string attributeName, string attributeValue, string requestedBy, CancellationToken token)
        {
            return RunModuleWithScopeAsync("IisAppPoolsAnalyzer", attributeName, attributeValue, requestedBy, token);
        }

        public Task<ModuleResult> RunProxyAddressesAsync(string attributeName, string attributeValue, string requestedBy, CancellationToken token)
        {
            return RunModuleWithScopeAsync("ProxyAddressAnalyzer", attributeName, attributeValue, requestedBy, token);
        }

        public Task<ModuleResult> RunTrustsAsync(string requestedBy, CancellationToken token)
        {
            return _brokerJobService.RunModuleAsync("TrustsAnalyzer", null, requestedBy, token);
        }

        public Task<ModuleResult> RunGpoAsync(string requestedBy, CancellationToken token)
        {
            return _brokerJobService.RunModuleAsync("GpoAnalyzer", null, requestedBy, token);
        }

        public Task<ModuleResult> RunDnsAsync(string requestedBy, CancellationToken token)
        {
            return _brokerJobService.RunModuleAsync("DnsAnalyzer", null, requestedBy, token);
        }

        private Task<ModuleResult> RunModuleWithScopeAsync(string moduleName, string attributeName, string attributeValue, string requestedBy, CancellationToken token)
        {
            var parameters = new Dictionary<string, string>
            {
                { "AttributeName", attributeName },
                { "AttributeValue", attributeValue }
            };

            return _brokerJobService.RunModuleAsync(moduleName, parameters, requestedBy, token);
        }
    }
}
