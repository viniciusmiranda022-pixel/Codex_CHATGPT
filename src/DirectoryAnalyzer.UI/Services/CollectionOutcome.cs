using System;
using System.Linq;
using DirectoryAnalyzer.Contracts;

namespace DirectoryAnalyzer.Services
{
    /// <summary>
    /// Desfecho de uma coleta, como a WPF deve exibi-lo. Separa "coleta válida sem itens" de
    /// "pedido recusado" e de "agente indisponível", que antes chegavam todos como 0 itens.
    /// </summary>
    public enum CollectionOutcomeKind
    {
        Completed = 0,
        CompletedEmpty = 1,
        Partial = 2,
        Rejected = 3,
        AgentUnavailable = 4,
        Failed = 5,
        ModuleUnavailable = 6,
        Canceled = 7
    }

    public sealed class CollectionOutcome
    {
        public const string GateExceptionType = "AgentGate";

        private CollectionOutcome(CollectionOutcomeKind kind, ModuleResult result, string code, string message)
        {
            Kind = kind;
            Result = result;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public CollectionOutcomeKind Kind { get; }

        /// <summary>ModuleResult devolvido pelo agente; null quando o pedido não chegou ao agente.</summary>
        public ModuleResult Result { get; }

        public string Code { get; }
        public string Message { get; }

        public int ItemCount => Result?.Items?.Count ?? 0;
        public int ErrorCount => Result?.Errors?.Count ?? (IsSuccess ? 0 : 1);

        /// <summary>True apenas quando o agente executou o módulo (com ou sem itens).</summary>
        public bool IsSuccess => Kind == CollectionOutcomeKind.Completed
            || Kind == CollectionOutcomeKind.CompletedEmpty
            || Kind == CollectionOutcomeKind.Partial;

        /// <summary>Texto registrado no painel (DashboardService).</summary>
        public string DashboardStatus
        {
            get
            {
                switch (Kind)
                {
                    case CollectionOutcomeKind.Completed: return "Concluído";
                    case CollectionOutcomeKind.CompletedEmpty: return "Concluído (0 itens)";
                    case CollectionOutcomeKind.Partial: return "Parcial";
                    case CollectionOutcomeKind.Rejected: return "Recusado";
                    case CollectionOutcomeKind.AgentUnavailable: return "Agente indisponível";
                    case CollectionOutcomeKind.ModuleUnavailable: return "Indisponível nesta versão";
                    case CollectionOutcomeKind.Canceled: return "Cancelado";
                    default: return "Com erros";
                }
            }
        }

        /// <summary>Mensagem exibida na tela, com o código quando houver.</summary>
        public string DescribeForUser(string itemNoun)
        {
            var codeSuffix = string.IsNullOrEmpty(Code) ? string.Empty : $" [{Code}]";
            switch (Kind)
            {
                case CollectionOutcomeKind.Completed:
                    return $"✅ Coleta concluída. {ItemCount} {itemNoun}.";
                case CollectionOutcomeKind.CompletedEmpty:
                    return $"✅ Coleta concluída pelo agente {Result?.AgentId}: nenhum {itemNoun} encontrado.";
                case CollectionOutcomeKind.Partial:
                    return $"⚠️ Coleta parcial: {ItemCount} {itemNoun}, {ErrorCount} erro(s).";
                case CollectionOutcomeKind.Rejected:
                    return $"⛔ Pedido recusado antes da execução{codeSuffix}: {Message}";
                case CollectionOutcomeKind.AgentUnavailable:
                    return $"🔌 Agente indisponível{codeSuffix}: {Message}";
                case CollectionOutcomeKind.ModuleUnavailable:
                    return $"🚧 Módulo temporariamente indisponível{codeSuffix}: {Message}";
                case CollectionOutcomeKind.Canceled:
                    return "⚠️ Coleta cancelada pelo usuário.";
                default:
                    return $"❌ Falha na coleta{codeSuffix}: {Message}";
            }
        }

        public static CollectionOutcome Rejected(string code, string message) => new CollectionOutcome(CollectionOutcomeKind.Rejected, null, code, message);

        public static CollectionOutcome AgentUnavailable(string code, string message) => new CollectionOutcome(CollectionOutcomeKind.AgentUnavailable, null, code, message);

        public static CollectionOutcome Failed(string code, string message) => new CollectionOutcome(CollectionOutcomeKind.Failed, null, code, message);

        public static CollectionOutcome ModuleUnavailable(string code, string message) => new CollectionOutcome(CollectionOutcomeKind.ModuleUnavailable, null, code, message);

        public static CollectionOutcome Canceled() => new CollectionOutcome(CollectionOutcomeKind.Canceled, null, "Canceled", "Cancelado pelo usuário.");

        /// <summary>Classifica o ModuleResult devolvido pelo agente.</summary>
        public static CollectionOutcome FromModuleResult(ModuleResult result)
        {
            if (result == null)
            {
                return Failed("ResultMissing", "O Broker não devolveu o resultado do job.");
            }

            var errors = result.Errors?.Where(e => e != null).ToList() ?? new System.Collections.Generic.List<ErrorInfo>();
            var items = result.Items?.Count ?? 0;

            var gate = errors.FirstOrDefault(e => string.Equals(e.ExceptionType, GateExceptionType, StringComparison.Ordinal));
            if (gate != null)
            {
                return new CollectionOutcome(CollectionOutcomeKind.Rejected, result, gate.Code, gate.Message);
            }

            if (errors.Count > 0 && items == 0)
            {
                return new CollectionOutcome(CollectionOutcomeKind.Failed, result, errors[0].Code, errors[0].Message);
            }

            if (errors.Count > 0)
            {
                return new CollectionOutcome(CollectionOutcomeKind.Partial, result, errors[0].Code, errors[0].Message);
            }

            return items == 0
                ? new CollectionOutcome(CollectionOutcomeKind.CompletedEmpty, result, null, result.Summary)
                : new CollectionOutcome(CollectionOutcomeKind.Completed, result, null, result.Summary);
        }
    }

    /// <summary>Lançada pelas APIs que devolvem ModuleResult quando o agente não executou a coleta.</summary>
    public sealed class CollectionNotExecutedException : InvalidOperationException
    {
        public CollectionNotExecutedException(CollectionOutcome outcome)
            : base(outcome?.DescribeForUser("itens"))
        {
            Outcome = outcome;
        }

        public CollectionOutcome Outcome { get; }
    }
}
