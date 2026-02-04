using System;
using System.Collections.Generic;
using System.DirectoryServices.AccountManagement;
using System.Threading;
using System.Threading.Tasks;
 codex/design-production-grade-on-premises-agent-architecture-mn24bx
using DirectoryAnalyzer.AgentContracts;

 main

namespace DirectoryAnalyzer.Agent
{
    public sealed class ActionRegistry
    {
        private readonly Dictionary<string, Func<AgentRequest, AgentConfig, CancellationToken, Task<AgentResponse>>> _actions;

        public ActionRegistry()
        {
            _actions = new Dictionary<string, Func<AgentRequest, AgentConfig, CancellationToken, Task<AgentResponse>>>(
                StringComparer.OrdinalIgnoreCase)
            {
                { "GetUsers", GetUsersAsync }
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
            var includeDisabled = false;
            if (request.Parameters != null && request.Parameters.TryGetValue("IncludeDisabled", out var includeDisabledRaw))
            {
                bool.TryParse(includeDisabledRaw, out includeDisabled);
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var results = new GetUsersResult();

            using var context = string.IsNullOrWhiteSpace(config.Domain)
                ? new PrincipalContext(ContextType.Domain)
                : new PrincipalContext(ContextType.Domain, config.Domain);
            using var searcher = new PrincipalSearcher(new UserPrincipal(context));

            foreach (var result in searcher.FindAll())
            {
                token.ThrowIfCancellationRequested();
                if (result is UserPrincipal user)
                {
                    var enabled = user.Enabled ?? false;
                    if (!includeDisabled && !enabled)
                    {
                        continue;
                    }

                    results.Users.Add(new UserRecord
                    {
                        SamAccountName = user.SamAccountName ?? string.Empty,
                        DisplayName = user.DisplayName ?? string.Empty,
                        Enabled = enabled
                    });
                }
            }

            stopwatch.Stop();
            return Task.FromResult(AgentResponse.Success(request.RequestId, stopwatch.ElapsedMilliseconds, results));
        }
    }
}
