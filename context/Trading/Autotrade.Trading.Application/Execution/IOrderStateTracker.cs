using Autotrade.Trading.Application.Contract.Execution;

namespace Autotrade.Trading.Application.Execution;

/// <summary>
/// 订单状态更新事件数据。
/// </summary>
public sealed record OrderStateUpdate
{
    /// <summary>
    /// 客户端订单 ID。
    /// </summary>
    public required string ClientOrderId { get; init; }

    /// <summary>
    /// 交易所订单 ID。
    /// </summary>
    public required string ExchangeOrderId { get; init; }

    /// <summary>
    /// 市场 ID。
    /// </summary>
    public string? MarketId { get; init; }

    /// <summary>
    /// Token ID。
    /// </summary>
    public string? TokenId { get; init; }

    /// <summary>
    /// 执行状态。
    /// </summary>
    public ExecutionStatus Status { get; init; }

    /// <summary>
    /// 原始数量。
    /// </summary>
    public decimal OriginalQuantity { get; init; }

    /// <summary>
    /// 已成交数量。
    /// </summary>
    public decimal FilledQuantity { get; init; }

    /// <summary>
    /// 成交均价。
    /// </summary>
    public decimal? AveragePrice { get; init; }

    /// <summary>
    /// 更新时间。
    /// </summary>
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 订单状态跟踪器接口：接收订单状态更新回调。
/// </summary>
public interface IOrderStateTracker
{
    /// <summary>
    /// 处理订单状态更新。
    /// </summary>
    /// <param name="update">状态更新事件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task OnOrderStateChangedAsync(OrderStateUpdate update, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定市场的当前挂单数。
    /// </summary>
    /// <param name="marketId">市场 ID。</param>
    /// <returns>挂单数量。</returns>
    int GetOpenOrderCount(string marketId);

    /// <summary>
    /// 获取所有市场的挂单数。
    /// </summary>
    /// <returns>市场 ID 到挂单数的映射。</returns>
    IReadOnlyDictionary<string, int> GetAllOpenOrderCounts();

    /// <summary>
    /// 获取所有仍处于挂单状态的订单快照。
    /// </summary>
    /// <returns>挂单订单列表。</returns>
    IReadOnlyList<OrderStateUpdate> GetOpenOrders();
}

/// <summary>
/// 空实现的订单状态跟踪器（用于不需要跟踪的场景）。
/// </summary>
public sealed class NullOrderStateTracker : IOrderStateTracker
{
    public static readonly NullOrderStateTracker Instance = new();

    private NullOrderStateTracker() { }

    public Task OnOrderStateChangedAsync(OrderStateUpdate update, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public int GetOpenOrderCount(string marketId) => 0;

    public IReadOnlyDictionary<string, int> GetAllOpenOrderCounts()
        => new Dictionary<string, int>();

    public IReadOnlyList<OrderStateUpdate> GetOpenOrders()
        => Array.Empty<OrderStateUpdate>();
}
