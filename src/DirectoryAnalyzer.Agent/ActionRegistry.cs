using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DirectoryAnalyzer.Agent.Contracts;
using DirectoryAnalyzer.Collectors;

namespace DirectoryAnalyzer.Agent
{
    public sealed class ActionRegistry
    {
        private static readonly HashSet<string> AllowedUserParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "IncludeDisabled",
            "LdapFilter"
        };

        private static readonly HashSet<string> NoParametersAllowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, Func<AgentRequest, AgentConfig, CancellationToken, Task<AgentResponse>>> _actions;
        private readonly PowerShellCollector _powerShellCollector = new PowerShellCollector();

        public ActionRegistry()
        {
            _actions = new Dictionary<string, Func<AgentRequest, AgentConfig, CancellationToken, Task<AgentResponse>>>(
                StringComparer.OrdinalIgnoreCase)
            {
                { "GetUsers", GetUsersAsync },
                { "GetGroups", GetGroupsAsync },
                { "GetComputers", GetComputersAsync },
                { "GetGpos", GetGposAsync },
                { "GetDnsZones", GetDnsZonesAsync },
                { "RunPowerShellScript", RunPowerShellScriptAsync }
            };
        }

        public async Task<AgentResponse> ExecuteAsync(AgentRequest request, AgentConfig config, CancellationToken token)
        {
            if (!_actions.TryGetValue(request.ActionName, out var action))
            {
                return AgentResponse.Failed(request.RequestId, "UnknownAction", "Action not permitted.");
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(config.ActionTimeoutSeconds));

            try
            {
                return await action(request, config, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return AgentResponse.Failed(request.RequestId, "Timeout", "Action exceeded time limit.");
            }
            catch (Exception ex)
            {
                return AgentResponse.FromException(request.RequestId, ex);
            }
        }

        private static Task<AgentResponse> GetUsersAsync(AgentRequest request, AgentConfig config, CancellationToken token)
        {
            if (!ValidateParameters(request.Parameters, AllowedUserParameters, out var error))
            {
                return Task.FromResult(AgentResponse.Failed(request.RequestId, "InvalidParameters", error));
            }

            if (!TryGetBooleanParameter(request.Parameters, "IncludeDisabled", out var includeDisabled, out error))
            {
                return Task.FromResult(AgentResponse.Failed(request.RequestId, "InvalidParameters", error));
            }

            if (!TryGetLdapFilter(request.Parameters, out var ldapFilter, out error))
            {
                return Task.FromResult(AgentResponse.Failed(request.RequestId, "InvalidParameters", error));
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var results = new GetUsersResult();

            var defaultNamingContext = GetDefaultNamingContext(config);
            using var entry = CreateDirectoryEntry(config, defaultNamingContext);
            using var searcher = CreateSearcher(entry);

            var filter = "(&(objectCategory=person)(objectClass=user)";
            if (!string.IsNullOrWhiteSpace(ldapFilter))
            {
                filter += ldapFilter;
            }

            if (!includeDisabled)
            {
                filter += "(!(userAccountControl:1.2.840.113556.1.4.803:=2))";
            }

            filter += ")";
            searcher.Filter = filter;
            searcher.PropertiesToLoad.AddRange(new[]
            {
                "samaccountname",
                "displayname",
                "userprincipalname",
                "distinguishedname",
                "objectsid",
                "useraccountcontrol"
            });

            foreach (SearchResult result in searcher.FindAll())
            {
                token.ThrowIfCancellationRequested();
                var enabled = !IsAccountDisabled(result);

                results.Users.Add(new UserRecord
                {
                    SamAccountName = GetPropertyString(result, "samaccountname"),
                    DisplayName = GetPropertyString(result, "displayname"),
                    Enabled = enabled,
                    DistinguishedName = GetPropertyString(result, "distinguishedname"),
                    UserPrincipalName = GetPropertyString(result, "userprincipalname"),
                    ObjectSid = GetPropertySid(result, "objectsid")
                });
            }

            stopwatch.Stop();
            return Task.FromResult(AgentResponse.Success(request.RequestId, stopwatch.ElapsedMilliseconds, results));
        }

        private static Task<AgentResponse> GetGroupsAsync(AgentRequest request, AgentConfig config, CancellationToken token)
        {
            if (!ValidateParameters(request.Parameters, NoParametersAllowed, out var error))
            {
                return Task.FromResult(AgentResponse.Failed(request.RequestId, "InvalidParameters", error));
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var results = new GetGroupsResult();

            var defaultNamingContext = GetDefaultNamingContext(config);
            using var entry = CreateDirectoryEntry(config, defaultNamingContext);
            using var searcher = CreateSearcher(entry);
            searcher.Filter = "(objectCategory=group)";
            searcher.PropertiesToLoad.AddRange(new[]
            {
                "name",
                "samaccountname",
                "distinguishedname",
                "description",
                "grouptype"
            });

            foreach (SearchResult result in searcher.FindAll())
            {
                token.ThrowIfCancellationRequested();
                results.Groups.Add(new GroupRecord
                {
                    Name = GetPropertyString(result, "name"),
                    SamAccountName = GetPropertyString(result, "samaccountname"),
                    DistinguishedName = GetPropertyString(result, "distinguishedname"),
                    Description = GetPropertyString(result, "description"),
                    GroupType = DescribeGroupType(result)
                });
            }

            stopwatch.Stop();
            return Task.FromResult(AgentResponse.Success(request.RequestId, stopwatch.ElapsedMilliseconds, results));
        }

        private static Task<AgentResponse> GetComputersAsync(AgentRequest request, AgentConfig config, CancellationToken token)
        {
            if (!ValidateParameters(request.Parameters, NoParametersAllowed, out var error))
            {
                return Task.FromResult(AgentResponse.Failed(request.RequestId, "InvalidParameters", error));
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var results = new GetComputersResult();

            var defaultNamingContext = GetDefaultNamingContext(config);
            using var entry = CreateDirectoryEntry(config, defaultNamingContext);
            using var searcher = CreateSearcher(entry);
            searcher.Filter = "(objectCategory=computer)";
            searcher.PropertiesToLoad.AddRange(new[]
            {
                "name",
                "samaccountname",
                "distinguishedname",
                "operatingsystem",
                "useraccountcontrol"
            });

            foreach (SearchResult result in searcher.FindAll())
            {
                token.ThrowIfCancellationRequested();
                results.Computers.Add(new ComputerRecord
                {
                    Name = GetPropertyString(result, "name"),
                    SamAccountName = GetPropertyString(result, "samaccountname"),
                    DistinguishedName = GetPropertyString(result, "distinguishedname"),
                    OperatingSystem = GetPropertyString(result, "operatingsystem"),
                    Enabled = !IsAccountDisabled(result)
                });
            }

            stopwatch.Stop();
            return Task.FromResult(AgentResponse.Success(request.RequestId, stopwatch.ElapsedMilliseconds, results));
        }

        private static Task<AgentResponse> GetGposAsync(AgentRequest request, AgentConfig config, CancellationToken token)
        {
            if (!ValidateParameters(request.Parameters, NoParametersAllowed, out var error))
            {
                return Task.FromResult(AgentResponse.Failed(request.RequestId, "InvalidParameters", error));
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var results = new GetGposResult();

            var defaultNamingContext = GetDefaultNamingContext(config);
            if (string.IsNullOrWhiteSpace(defaultNamingContext))
            {
                return Task.FromResult(AgentResponse.Failed(request.RequestId, "DirectoryError", "Default naming context not available."));
            }

            var policiesDn = $"CN=Policies,CN=System,{defaultNamingContext}";
            using var entry = CreateDirectoryEntry(config, policiesDn);
            using var searcher = CreateSearcher(entry);
            searcher.Filter = "(objectClass=groupPolicyContainer)";
            searcher.PropertiesToLoad.AddRange(new[]
            {
                "displayname",
                "distinguishedname",
                "objectguid",
                "gpcfilesyspath"
            });

            foreach (SearchResult result in searcher.FindAll())
            {
                token.ThrowIfCancellationRequested();
                results.Gpos.Add(new GpoRecord
                {
                    Name = GetPropertyString(result, "displayname"),
                    DistinguishedName = GetPropertyString(result, "distinguishedname"),
                    Guid = GetPropertyGuid(result, "objectguid"),
                    FileSystemPath = GetPropertyString(result, "gpcfilesyspath")
                });
            }

            stopwatch.Stop();
            return Task.FromResult(AgentResponse.Success(request.RequestId, stopwatch.ElapsedMilliseconds, results));
        }

        private static Task<AgentResponse> GetDnsZonesAsync(AgentRequest request, AgentConfig config, CancellationToken token)
        {
            if (!ValidateParameters(request.Parameters, NoParametersAllowed, out var error))
            {
                return Task.FromResult(AgentResponse.Failed(request.RequestId, "InvalidParameters", error));
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var results = new GetDnsZonesResult();

            var contexts = GetDnsNamingContexts(config);
            foreach (var namingContext in contexts)
            {
                token.ThrowIfCancellationRequested();
                var baseDn = $"CN=MicrosoftDNS,{namingContext}";
                using var entry = CreateDirectoryEntry(config, baseDn);
                using var searcher = CreateSearcher(entry);
                searcher.Filter = "(objectClass=dnsZone)";
                searcher.PropertiesToLoad.AddRange(new[]
                {
                    "name",
                    "distinguishedname",
                    "dnszonetype",
                    "isdsintegrated"
                });

                foreach (SearchResult result in searcher.FindAll())
                {
                    token.ThrowIfCancellationRequested();
                    results.Zones.Add(new DnsZoneRecord
                    {
                        Name = GetPropertyString(result, "name"),
                        DistinguishedName = GetPropertyString(result, "distinguishedname"),
                        ZoneType = GetPropertyString(result, "dnszonetype"),
                        IsDsIntegrated = GetPropertyBoolean(result, "isdsintegrated") ?? true
                    });
                }
            }

            stopwatch.Stop();
            return Task.FromResult(AgentResponse.Success(request.RequestId, stopwatch.ElapsedMilliseconds, results));
        }

        private Task<AgentResponse> RunPowerShellScriptAsync(AgentRequest request, AgentConfig config, CancellationToken token)
        {
            if (!request.Parameters.TryGetValue("Script", out var scriptText) || string.IsNullOrWhiteSpace(scriptText))
            {
                return Task.FromResult(AgentResponse.Failed(request.RequestId, "InvalidParameters", "Script is required."));
            }

            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in request.Parameters)
            {
                if (string.Equals(pair.Key, "Script", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                parameters[pair.Key] = pair.Value;
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var results = _powerShellCollector.Execute(scriptText, parameters, out var errors);
            stopwatch.Stop();

            if (errors.Count > 0)
            {
                return Task.FromResult(AgentResponse.Failed(request.RequestId, "PowerShellError", string.Join(" | ", errors)));
            }

            return Task.FromResult(AgentResponse.Success(request.RequestId, stopwatch.ElapsedMilliseconds, results));
        }

        private static bool ValidateParameters(Dictionary<string, string> parameters, HashSet<string> allowedKeys, out string error)
        {
            error = string.Empty;
            if (parameters == null || parameters.Count == 0)
            {
                return true;
            }

            foreach (var key in parameters.Keys)
            {
                if (!allowedKeys.Contains(key))
                {
                    error = $"Parameter '{key}' is not allowed.";
                    return false;
                }
            }

            return true;
        }

        private static bool TryGetBooleanParameter(Dictionary<string, string> parameters, string key, out bool value, out string error)
        {
            value = false;
            error = string.Empty;
            if (parameters == null || !parameters.TryGetValue(key, out var raw))
            {
                return true;
            }

            if (!bool.TryParse(raw, out value))
            {
                error = $"Parameter '{key}' must be a boolean value.";
                return false;
            }

            return true;
        }

        private static bool TryGetLdapFilter(Dictionary<string, string> parameters, out string filter, out string error)
        {
            filter = string.Empty;
            error = string.Empty;
            if (parameters == null || !parameters.TryGetValue("LdapFilter", out var raw))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                error = "LdapFilter cannot be empty.";
                return false;
            }

            var trimmed = raw.Trim();
            if (trimmed.Length > 512)
            {
                error = "LdapFilter exceeds the maximum length of 512 characters.";
                return false;
            }

            if (!trimmed.StartsWith("(", StringComparison.Ordinal) || !trimmed.EndsWith(")", StringComparison.Ordinal))
            {
                error = "LdapFilter must start with '(' and end with ')'.";
                return false;
            }

            if (!IsLdapFilterSafe(trimmed))
            {
                error = "LdapFilter contains invalid characters or unmatched parentheses.";
                return false;
            }

            filter = trimmed;
            return true;
        }

        private static bool IsLdapFilterSafe(string filter)
        {
            var allowed = new Regex(@"^[a-zA-Z0-9=\*\(\)\&\|\!\-\._@\s\\:;,\+/]+$");
            if (!allowed.IsMatch(filter))
            {
                return false;
            }

            var depth = 0;
            foreach (var ch in filter)
            {
                if (ch == '(')
                {
                    depth++;
                }
                else if (ch == ')')
                {
                    depth--;
                    if (depth < 0)
                    {
                        return false;
                    }
                }
            }

            return depth == 0;
        }

        private static DirectorySearcher CreateSearcher(DirectoryEntry entry)
        {
            return new DirectorySearcher(entry)
            {
                SearchScope = SearchScope.Subtree,
                PageSize = 500
            };
        }

        private static string GetDefaultNamingContext(AgentConfig config)
        {
            using var rootDse = new DirectoryEntry(string.IsNullOrWhiteSpace(config.Domain)
                ? "LDAP://RootDSE"
                : $"LDAP://{config.Domain}/RootDSE");
            return rootDse.Properties["defaultNamingContext"]?.Value as string ?? string.Empty;
        }

        private static List<string> GetDnsNamingContexts(AgentConfig config)
        {
            var contexts = new List<string>();
            using var rootDse = new DirectoryEntry(string.IsNullOrWhiteSpace(config.Domain)
                ? "LDAP://RootDSE"
                : $"LDAP://{config.Domain}/RootDSE");

            var domainDns = rootDse.Properties["domainDnsZones"]?.Value as string;
            var forestDns = rootDse.Properties["forestDnsZones"]?.Value as string;
            var defaultNamingContext = rootDse.Properties["defaultNamingContext"]?.Value as string;
            var forestNamingContext = rootDse.Properties["forestNamingContext"]?.Value as string;

            if (!string.IsNullOrWhiteSpace(domainDns))
            {
                contexts.Add(domainDns);
            }
            else if (!string.IsNullOrWhiteSpace(defaultNamingContext))
            {
                contexts.Add($"DC=DomainDnsZones,{defaultNamingContext}");
            }

            if (!string.IsNullOrWhiteSpace(forestDns))
            {
                contexts.Add(forestDns);
            }
            else if (!string.IsNullOrWhiteSpace(forestNamingContext))
            {
                contexts.Add($"DC=ForestDnsZones,{forestNamingContext}");
            }

            return contexts;
        }

        private static DirectoryEntry CreateDirectoryEntry(AgentConfig config, string distinguishedName)
        {
            var prefix = string.IsNullOrWhiteSpace(config.Domain)
                ? "LDAP://"
                : $"LDAP://{config.Domain}/";
            return new DirectoryEntry($"{prefix}{distinguishedName}");
        }

        private static string GetPropertyString(SearchResult result, string propertyName)
        {
            if (result.Properties.Contains(propertyName) && result.Properties[propertyName].Count > 0)
            {
                return result.Properties[propertyName][0]?.ToString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static bool? GetPropertyBoolean(SearchResult result, string propertyName)
        {
            if (result.Properties.Contains(propertyName) && result.Properties[propertyName].Count > 0)
            {
                if (result.Properties[propertyName][0] is bool flag)
                {
                    return flag;
                }

                if (bool.TryParse(result.Properties[propertyName][0]?.ToString(), out var parsed))
                {
                    return parsed;
                }
            }

            return null;
        }

        private static string GetPropertySid(SearchResult result, string propertyName)
        {
            if (result.Properties.Contains(propertyName) && result.Properties[propertyName].Count > 0)
            {
                if (result.Properties[propertyName][0] is byte[] bytes)
                {
                    return new SecurityIdentifier(bytes, 0).Value;
                }

                return result.Properties[propertyName][0]?.ToString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static string GetPropertyGuid(SearchResult result, string propertyName)
        {
            if (result.Properties.Contains(propertyName) && result.Properties[propertyName].Count > 0)
            {
                if (result.Properties[propertyName][0] is byte[] bytes)
                {
                    return new Guid(bytes).ToString();
                }

                if (Guid.TryParse(result.Properties[propertyName][0]?.ToString(), out var guid))
                {
                    return guid.ToString();
                }
            }

            return string.Empty;
        }

        private static bool IsAccountDisabled(SearchResult result)
        {
            if (result.Properties.Contains("useraccountcontrol") && result.Properties["useraccountcontrol"].Count > 0)
            {
                if (int.TryParse(result.Properties["useraccountcontrol"][0]?.ToString(), out var uac))
                {
                    return (uac & 0x2) != 0;
                }
            }

            return false;
        }

        private static string DescribeGroupType(SearchResult result)
        {
            if (result.Properties.Contains("grouptype") && result.Properties["grouptype"].Count > 0
                && int.TryParse(result.Properties["grouptype"][0]?.ToString(), out var groupType))
            {
                var scope = (groupType & 0x8) != 0 ? "Universal"
                    : (groupType & 0x4) != 0 ? "DomainLocal"
                    : (groupType & 0x2) != 0 ? "Global"
                    : "Unknown";
                var security = (groupType & unchecked((int)0x80000000)) != 0 ? "Security" : "Distribution";
                return $"{scope}/{security}";
            }

            return string.Empty;
        }
    }
}
