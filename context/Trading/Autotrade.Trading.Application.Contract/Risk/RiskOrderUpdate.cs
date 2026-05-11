using Autotrade.Trading.Application.Contract.Execution;

namespace Autotrade.Trading.Application.Contract.Risk;

/// <summary>
/// Risk update when order state changes.
/// </summary>
public sealed record RiskOrderUpdate
{
    public required string ClientOrderId { get; init; }

    /// <summary>
    /// 策略 ID（用于 post-trade 风险检查）。
    /// </summary>
    public string? StrategyId { get; init; }

    public required ExecutionStatus Status { get; init; }

    public decimal FilledQuantity { get; init; }

    public decimal OriginalQuantity { get; init; }

    /// <summary>
    /// 成交价格（用于 PnL 计算）。
    /// </summary>
    public decimal? FilledPrice { get; init; }

    /// <summary>
    /// 市场 ID（用于定位）。
    /// </summary>
    public string? MarketId { get; init; }
}
