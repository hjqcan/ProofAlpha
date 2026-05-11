namespace Autotrade.Strategy.Application.Contract.Strategies;

/// <summary>
/// Structured observation emitted by a strategy evaluation or execution step.
/// </summary>
public sealed record StrategyObservation(
    string StrategyId,
    string? MarketId,
    string Phase,
    string Outcome,
    string ReasonCode,
    string? FeaturesJson,
    string? StateJson,
    string? CorrelationId,
    string ConfigVersion,
    string? ExecutionMode,
    DateTimeOffset TimestampUtc);
