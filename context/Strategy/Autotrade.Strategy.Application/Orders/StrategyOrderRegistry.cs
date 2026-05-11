// ============================================================================
// 策略订单注册表
// ============================================================================
// 跟踪所有活跃订单的信息，用于状态轮询和回调路由。
// ============================================================================

using System.Collections.Concurrent;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Strategy.Application.Orders;

/// <summary>
/// 策略订单信息。
/// </summary>
public sealed record StrategyOrderInfo(
    string StrategyId,
    string MarketId,
    string TokenId,
    OutcomeSide Outcome,
    OrderLeg Leg,
    StrategySignalType SignalType,
    OrderSide Side,
    OrderType OrderType,
    TimeInForce TimeInForce,
    decimal Price,
    decimal OriginalQuantity,
    DateTimeOffset CreatedAtUtc,
    ExecutionStatus LastStatus,
    decimal LastFilledQuantity);

/// <summary>
/// 策略订单注册表。
/// 跟踪所有活跃订单的信息。
/// </summary>
public sealed class StrategyOrderRegistry
{
    private readonly ConcurrentDictionary<string, StrategyOrderInfo> _orders = new();

    /// <summary>
    /// 注册订单。
    /// </summary>
    public void Register(string clientOrderId, StrategyOrderInfo info)
    {
        if (string.IsNullOrWhiteSpace(clientOrderId))
        {
            throw new ArgumentException("ClientOrderId cannot be empty.", nameof(clientOrderId));
        }

        _orders[clientOrderId] = info;
    }

    /// <summary>
    /// 尝试获取订单信息。
    /// </summary>
    public bool TryGet(string clientOrderId, out StrategyOrderInfo info)
    {
        return _orders.TryGetValue(clientOrderId, out info!);
    }

    /// <summary>
    /// 获取所有活跃订单。
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, StrategyOrderInfo>> GetOpenOrders()
    {
        return _orders.ToArray();
    }

    /// <summary>
    /// 更新订单状态。
    /// </summary>
    public void UpdateStatus(string clientOrderId, ExecutionStatus status, decimal filledQuantity)
    {
        if (_orders.TryGetValue(clientOrderId, out var info))
        {
            _orders[clientOrderId] = info with { LastStatus = status, LastFilledQuantity = filledQuantity };
        }
    }

    /// <summary>
    /// 移除订单。
    /// </summary>
    public void Remove(string clientOrderId)
    {
        _orders.TryRemove(clientOrderId, out _);
    }
}
