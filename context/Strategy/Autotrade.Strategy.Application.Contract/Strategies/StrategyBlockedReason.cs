namespace Autotrade.Strategy.Application.Contract.Strategies;

/// <summary>
/// Operator-facing reason that explains why a strategy cannot currently act.
/// </summary>
public sealed record StrategyBlockedReason(
    StrategyBlockedReasonKind Kind,
    string Code,
    string Message);

public enum StrategyBlockedReasonKind
{
    None = 0,
    KillSwitch = 1,
    DisabledConfig = 2,
    RiskLimit = 3,
    Readiness = 4,
    StaleData = 5,
    MissingMarket = 6,
    StrategyFault = 7
}

public static class StrategyBlockedReasons
{
    public static StrategyBlockedReason KillSwitch(string? message = null)
        => new(
            StrategyBlockedReasonKind.KillSwitch,
            "kill_switch",
            string.IsNullOrWhiteSpace(message)
                ? "A global or strategy kill switch is active."
                : message.Trim());

    public static StrategyBlockedReason DisabledConfig(string strategyId)
        => new(
            StrategyBlockedReasonKind.DisabledConfig,
            "disabled_config",
            $"Strategy {strategyId} is disabled in configuration.");

    public static StrategyBlockedReason RiskLimit(string? message = null)
        => new(
            StrategyBlockedReasonKind.RiskLimit,
            "risk_limit",
            string.IsNullOrWhiteSpace(message)
                ? "A risk limit prevents this strategy from opening new exposure."
                : message.Trim());

    public static StrategyBlockedReason Readiness(string? message = null)
        => new(
            StrategyBlockedReasonKind.Readiness,
            "readiness",
            string.IsNullOrWhiteSpace(message)
                ? "A required readiness check is not passing."
                : message.Trim());

    public static StrategyBlockedReason StaleData(string? message = null)
        => new(
            StrategyBlockedReasonKind.StaleData,
            "stale_data",
            string.IsNullOrWhiteSpace(message)
                ? "Required market data is stale."
                : message.Trim());

    public static StrategyBlockedReason MissingMarket(string? marketId = null)
        => new(
            StrategyBlockedReasonKind.MissingMarket,
            "missing_market",
            string.IsNullOrWhiteSpace(marketId)
                ? "A required market is not available."
                : $"Market {marketId.Trim()} is not available.");

    public static StrategyBlockedReason StrategyFault(string? message = null)
        => new(
            StrategyBlockedReasonKind.StrategyFault,
            "strategy_fault",
            string.IsNullOrWhiteSpace(message)
                ? "The strategy faulted and requires operator attention."
                : message.Trim());
}
