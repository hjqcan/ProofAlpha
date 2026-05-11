using NetDevPack.Domain;

namespace Autotrade.SelfImprove.Domain.Entities;

public sealed class StrategyEpisode : Entity, IAggregateRoot
{
    private StrategyEpisode()
    {
        StrategyId = string.Empty;
        ConfigVersion = string.Empty;
        WindowStartUtc = DateTimeOffset.UtcNow;
        WindowEndUtc = DateTimeOffset.UtcNow;
        SourceIdsJson = "{}";
        MetricsJson = "{}";
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public StrategyEpisode(
        string strategyId,
        string? marketId,
        string configVersion,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        int decisionCount,
        int observationCount,
        int orderCount,
        int tradeCount,
        int riskEventCount,
        decimal netPnl,
        decimal fillRate,
        decimal rejectRate,
        decimal timeoutRate,
        decimal maxOpenExposure,
        decimal drawdownLike,
        string sourceIdsJson,
        string metricsJson,
        DateTimeOffset createdAtUtc)
    {
        StrategyId = string.IsNullOrWhiteSpace(strategyId)
            ? throw new ArgumentException("StrategyId cannot be empty.", nameof(strategyId))
            : strategyId.Trim();
        MarketId = string.IsNullOrWhiteSpace(marketId) ? null : marketId.Trim();
        ConfigVersion = string.IsNullOrWhiteSpace(configVersion) ? "unknown" : configVersion.Trim();
        WindowStartUtc = windowStartUtc;
        WindowEndUtc = windowEndUtc <= windowStartUtc
            ? throw new ArgumentException("WindowEndUtc must be after WindowStartUtc.", nameof(windowEndUtc))
            : windowEndUtc;
        DecisionCount = Math.Max(0, decisionCount);
        ObservationCount = Math.Max(0, observationCount);
        OrderCount = Math.Max(0, orderCount);
        TradeCount = Math.Max(0, tradeCount);
        RiskEventCount = Math.Max(0, riskEventCount);
        NetPnl = netPnl;
        FillRate = ClampRate(fillRate);
        RejectRate = ClampRate(rejectRate);
        TimeoutRate = ClampRate(timeoutRate);
        MaxOpenExposure = Math.Max(0, maxOpenExposure);
        DrawdownLike = Math.Max(0, drawdownLike);
        SourceIdsJson = string.IsNullOrWhiteSpace(sourceIdsJson) ? "{}" : sourceIdsJson.Trim();
        MetricsJson = string.IsNullOrWhiteSpace(metricsJson) ? "{}" : metricsJson.Trim();
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc;
    }

    public string StrategyId { get; private set; }

    public string? MarketId { get; private set; }

    public string ConfigVersion { get; private set; }

    public DateTimeOffset WindowStartUtc { get; private set; }

    public DateTimeOffset WindowEndUtc { get; private set; }

    public int DecisionCount { get; private set; }

    public int ObservationCount { get; private set; }

    public int OrderCount { get; private set; }

    public int TradeCount { get; private set; }

    public int RiskEventCount { get; private set; }

    public decimal NetPnl { get; private set; }

    public decimal FillRate { get; private set; }

    public decimal RejectRate { get; private set; }

    public decimal TimeoutRate { get; private set; }

    public decimal MaxOpenExposure { get; private set; }

    public decimal DrawdownLike { get; private set; }

    public string SourceIdsJson { get; private set; }

    public string MetricsJson { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    private static decimal ClampRate(decimal value)
    {
        if (value < 0)
        {
            return 0;
        }

        return value > 1 ? 1 : value;
    }
}
