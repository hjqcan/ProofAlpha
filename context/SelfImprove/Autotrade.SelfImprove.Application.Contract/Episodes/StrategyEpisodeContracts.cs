namespace Autotrade.SelfImprove.Application.Contract.Episodes;

public sealed record BuildStrategyEpisodeRequest(
    string StrategyId,
    string? MarketId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    int Limit = 5000);

public sealed record StrategyEpisodeDto(
    Guid Id,
    string StrategyId,
    string? MarketId,
    string ConfigVersion,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    int DecisionCount,
    int ObservationCount,
    int OrderCount,
    int TradeCount,
    int RiskEventCount,
    decimal NetPnl,
    decimal FillRate,
    decimal RejectRate,
    decimal TimeoutRate,
    decimal MaxOpenExposure,
    decimal DrawdownLike,
    string SourceIdsJson,
    string MetricsJson,
    DateTimeOffset CreatedAtUtc);
