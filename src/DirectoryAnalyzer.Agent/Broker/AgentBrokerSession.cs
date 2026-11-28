using System;
using System.Threading;
using System.Threading.Tasks;
using DirectoryAnalyzer.Contracts;
using Microsoft.AspNetCore.SignalR.Client;

namespace DirectoryAnalyzer.Agent.Broker
{
    /// <summary>
    /// Sessão de saída do agente com o Broker. A conexão é sempre iniciada pelo agente. Cada conexão
    /// SignalR tem um ConnectionId novo, inclusive após reconexão automática; por isso o agente refaz o
    /// AgentConnect toda vez que a conexão é restabelecida. Sem isso, o Broker não encontra o agente e
    /// novos jobs recebem AgentUnavailable mesmo com o transporte ativo.
    /// </summary>
    public sealed class AgentBrokerSession
    {
        public const string DispatchJobMethod = "DispatchJob";
        public const string AgentConnectMethod = "AgentConnect";

        private readonly HubConnection _connection;
        private readonly Func<AgentDescriptor> _descriptorFactory;
        private readonly Action<string> _log;
        private int _registrations;

        public AgentBrokerSession(
            HubConnection connection,
            Func<AgentDescriptor> descriptorFactory,
            Func<string, JobRequest, Task> onJob,
            Action<string> log = null)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _descriptorFactory = descriptorFactory ?? throw new ArgumentNullException(nameof(descriptorFactory));
            if (onJob == null)
            {
                throw new ArgumentNullException(nameof(onJob));
            }

            _log = log ?? (_ => { });
            _connection.On<string, JobRequest>(DispatchJobMethod, (jobId, request) => onJob(jobId, request));
            _connection.Reconnected += OnReconnectedAsync;
        }

        public HubConnection Connection => _connection;

        /// <summary>Quantas vezes o agente se registrou no Broker (1 na partida + 1 por reconexão).</summary>
        public int Registrations => Volatile.Read(ref _registrations);

        public async Task StartAsync(CancellationToken token)
        {
            await _connection.StartAsync(token).ConfigureAwait(false);
            await RegisterAsync(token).ConfigureAwait(false);
        }

        private async Task OnReconnectedAsync(string connectionId)
        {
            try
            {
                await RegisterAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Conexão ativa sem registro não recebe jobs: encerra para que o laço do worker reinicie.
                _log($"Falha ao refazer AgentConnect após reconexão ({ex.GetType().Name}); reiniciando a sessão.");
                await _connection.StopAsync().ConfigureAwait(false);
            }
        }

        private async Task RegisterAsync(CancellationToken token)
        {
            await _connection.InvokeAsync(AgentConnectMethod, _descriptorFactory(), token).ConfigureAwait(false);
            var count = Interlocked.Increment(ref _registrations);
            _log(count == 1 ? "Registrado no Broker." : $"Registro refeito no Broker após reconexão (#{count}).");
        }
    }

    /// <summary>
    /// Reconexão sem limite de tentativas: o padrão do SignalR desiste após ~42 s e deixaria o serviço
    /// rodando sem conexão. O intervalo cresce até <see cref="MaxDelay"/>.
    /// </summary>
    public sealed class UnboundedRetryPolicy : IRetryPolicy
    {
        public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);
        private readonly TimeSpan[] _steps;

        public UnboundedRetryPolicy(params TimeSpan[] steps)
        {
            _steps = steps != null && steps.Length > 0
                ? steps
                : new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), MaxDelay };
        }

        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            var index = (int)Math.Min(retryContext.PreviousRetryCount, _steps.Length - 1);
            return _steps[index];
        }
    }
}
