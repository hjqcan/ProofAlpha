namespace Autotrade.Strategy.Application.Decisions;

public sealed record StrategyDecisionRecord(
    Guid DecisionId,
    string StrategyId,
    string Action,
    string Reason,
    string? MarketId,
    string? ContextJson,
    DateTimeOffset TimestampUtc,
    string ConfigVersion,
    string? CorrelationId = null,
    string? ExecutionMode = null,
    Guid? RunSessionId = null);
