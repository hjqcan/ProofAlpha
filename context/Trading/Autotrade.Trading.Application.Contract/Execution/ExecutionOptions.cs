namespace Autotrade.Trading.Application.Contract.Execution;

/// <summary>
/// 执行模式。
/// </summary>
public enum ExecutionMode
{
    /// <summary>实盘交易。</summary>
    Live = 1,

    /// <summary>模拟交易。</summary>
    Paper = 2
}

/// <summary>
/// 执行服务配置选项。
/// </summary>
public sealed class ExecutionOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "Execution";

    /// <summary>
    /// 执行模式（Live/Paper）。
    /// </summary>
    public ExecutionMode Mode { get; set; } = ExecutionMode.Paper;

    /// <summary>
    /// 默认订单超时时间（秒）。
    /// </summary>
    public int DefaultTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 每个市场的最大挂单数。
    /// </summary>
    public int MaxOpenOrdersPerMarket { get; set; } = 10;

    /// <summary>
    /// 是否启用订单对账。
    /// </summary>
    public bool EnableReconciliation { get; set; } = true;

    /// <summary>
    /// 订单对账间隔（秒）。
    /// </summary>
    public int ReconciliationIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// 幂等性缓存 TTL（秒）。
    /// </summary>
    public int IdempotencyTtlSeconds { get; set; } = 86400; // 24 hours

    /// <summary>
    /// 是否优先使用交易所批量下单接口。
    /// </summary>
    public bool UseBatchOrders { get; set; } = false;

    /// <summary>
    /// 单次批量下单的最大订单数。Polymarket 当前常见限制为 15。
    /// </summary>
    public int MaxBatchOrderSize { get; set; } = 15;

    /// <summary>
    /// Enable low-latency CLOB user order/trade WebSocket events in Live mode.
    /// </summary>
    public bool EnableUserOrderEvents { get; set; } = true;

    /// <summary>
    /// Interval for refreshing user WebSocket market subscriptions from persisted open orders.
    /// </summary>
    public int UserOrderEventSubscriptionRefreshSeconds { get; set; } = 10;
}
