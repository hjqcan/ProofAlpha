namespace Autotrade.Strategy.Application.Observations;

public sealed record StrategyObservationQuery(
    string? StrategyId = null,
    string? MarketId = null,
    string? Phase = null,
    string? Outcome = null,
    string? ReasonCode = null,
    string? ConfigVersion = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    int Limit = 500);
