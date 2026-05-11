namespace Autotrade.Strategy.Application.Contract.Strategies;

/// <summary>
/// Strategy decision record for audit.
/// </summary>
public sealed record StrategyDecision(
    string StrategyId,
    string Action,
    string Reason,
    string? MarketId,
    string? ContextJson,
    DateTimeOffset TimestampUtc,
    string? CorrelationId = null,
    string? ExecutionMode = null,
    Guid? RunSessionId = null);
