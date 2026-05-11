// ============================================================================
// Paper 订单存储
// ============================================================================
// 用于跨请求共享 Paper Trading 的订单状态。
// ============================================================================

using System.Collections.Concurrent;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Trading.Application.Execution;

/// <summary>
/// Paper 订单存储（Singleton）。
/// 用于跨请求共享订单状态。
/// </summary>
public sealed class PaperOrderStore
{
    private readonly ConcurrentDictionary<string, PaperOrder> _orders = new();
    private readonly string _instanceId = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..30];
    private int _orderIdCounter;

    /// <summary>
    /// 生成新的交易所订单 ID。
    /// </summary>
    public string GenerateExchangeOrderId()
    {
        return $"PAPER-{_instanceId}-{Interlocked.Increment(ref _orderIdCounter):D8}";
    }

    /// <summary>
    /// 添加或更新订单。
    /// </summary>
    public void AddOrUpdate(string clientOrderId, PaperOrder order)
    {
        _orders[clientOrderId] = order;
    }

    /// <summary>
    /// 尝试获取订单。
    /// </summary>
    public bool TryGet(string clientOrderId, out PaperOrder? order)
    {
        return _orders.TryGetValue(clientOrderId, out order);
    }

    /// <summary>
    /// 获取所有订单。
    /// </summary>
    public IReadOnlyDictionary<string, PaperOrder> GetAll()
    {
        return _orders;
    }
}

/// <summary>
/// 模拟订单内部状态。
/// </summary>
public sealed class PaperOrder
{
    public required string ClientOrderId { get; init; }
    public required string ExchangeOrderId { get; init; }
    public required Guid TradingAccountId { get; init; }
    public required string TokenId { get; init; }
    public required string MarketId { get; init; }
    public string? StrategyId { get; init; }
    public string? CorrelationId { get; init; }
    public required OutcomeSide Outcome { get; init; }
    public required OrderSide Side { get; init; }
    public required OrderType OrderType { get; init; }
    public required TimeInForce TimeInForce { get; init; }
    public bool NegRisk { get; init; }
    public required decimal Price { get; init; }
    public required decimal OriginalQuantity { get; init; }
    public decimal FilledQuantity { get; set; }
    public decimal? AverageFilledPrice { get; set; }
    public ExecutionStatus Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public DateTimeOffset? GoodTilDateUtc { get; init; }
}
