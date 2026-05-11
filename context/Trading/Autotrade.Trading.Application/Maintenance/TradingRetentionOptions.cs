namespace Autotrade.Trading.Application.Maintenance;

/// <summary>
/// 交易数据保留配置。
/// </summary>
public sealed class TradingRetentionOptions
{
    public const string SectionName = "Trading:Retention";

    /// <summary>
    /// 订单保留天数（0 = 不清理）。
    /// </summary>
    public int OrderRetentionDays { get; set; } = 90;

    /// <summary>
    /// 成交保留天数（0 = 不清理）。
    /// </summary>
    public int TradeRetentionDays { get; set; } = 365;

    /// <summary>
    /// 订单事件保留天数（0 = 不清理）。
    /// </summary>
    public int OrderEventRetentionDays { get; set; } = 30;

    /// <summary>
    /// 风控事件保留天数（0 = 不清理）。
    /// </summary>
    public int RiskEventRetentionDays { get; set; } = 90;

    public void Validate()
    {
        if (OrderRetentionDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OrderRetentionDays), OrderRetentionDays, "OrderRetentionDays must be >= 0.");
        }

        if (TradeRetentionDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(TradeRetentionDays), TradeRetentionDays, "TradeRetentionDays must be >= 0.");
        }

        if (OrderEventRetentionDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OrderEventRetentionDays), OrderEventRetentionDays, "OrderEventRetentionDays must be >= 0.");
        }

        if (RiskEventRetentionDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RiskEventRetentionDays), RiskEventRetentionDays, "RiskEventRetentionDays must be >= 0.");
        }
    }
}
