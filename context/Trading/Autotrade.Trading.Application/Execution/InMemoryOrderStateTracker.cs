using System.Collections.Concurrent;
using Autotrade.Trading.Application.Contract.Execution;
using Microsoft.Extensions.Logging;

namespace Autotrade.Trading.Application.Execution;

/// <summary>
/// 内存订单状态跟踪器：跟踪订单状态变化和每个市场的挂单数。
/// </summary>
public sealed class InMemoryOrderStateTracker : IOrderStateTracker
{
    private readonly ILogger<InMemoryOrderStateTracker> _logger;

    /// <summary>
    /// 订单状态缓存：ClientOrderId -> OrderStateUpdate
    /// </summary>
    private readonly ConcurrentDictionary<string, OrderStateUpdate> _orders = new();

    /// <summary>
    /// 每个市场的挂单数：MarketId -> Count
    /// </summary>
    private readonly ConcurrentDictionary<string, int> _openOrderCounts = new();

    public InMemoryOrderStateTracker(ILogger<InMemoryOrderStateTracker> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task OnOrderStateChangedAsync(OrderStateUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var previousState = _orders.GetValueOrDefault(update.ClientOrderId);
        _orders[update.ClientOrderId] = update;

        // 更新挂单计数
        if (update.MarketId is not null)
        {
            var wasOpen = previousState is not null && IsOpenStatus(previousState.Status);
            var isOpen = IsOpenStatus(update.Status);

            if (!wasOpen && isOpen)
            {
                // 新挂单
                _openOrderCounts.AddOrUpdate(update.MarketId, 1, (_, count) => count + 1);
                _logger.LogDebug(
                    "挂单数增加: MarketId={MarketId}, ClientOrderId={ClientOrderId}, Count={Count}",
                    update.MarketId, update.ClientOrderId, _openOrderCounts.GetValueOrDefault(update.MarketId));
            }
            else if (wasOpen && !isOpen)
            {
                // 订单完成/取消
                _openOrderCounts.AddOrUpdate(update.MarketId, 0, (_, count) => Math.Max(0, count - 1));
                _logger.LogDebug(
                    "挂单数减少: MarketId={MarketId}, ClientOrderId={ClientOrderId}, Count={Count}",
                    update.MarketId, update.ClientOrderId, _openOrderCounts.GetValueOrDefault(update.MarketId));
            }
        }

        _logger.LogInformation(
            "订单状态更新: ClientOrderId={ClientOrderId}, ExchangeOrderId={ExchangeOrderId}, " +
            "Status={Status}, Filled={Filled}/{Original}",
            update.ClientOrderId,
            update.ExchangeOrderId,
            update.Status,
            update.FilledQuantity,
            update.OriginalQuantity);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public int GetOpenOrderCount(string marketId)
    {
        if (string.IsNullOrWhiteSpace(marketId))
        {
            return 0;
        }

        return _openOrderCounts.GetValueOrDefault(marketId);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, int> GetAllOpenOrderCounts()
    {
        return new Dictionary<string, int>(_openOrderCounts);
    }

    /// <inheritdoc />
    public IReadOnlyList<OrderStateUpdate> GetOpenOrders()
    {
        return _orders.Values
            .Where(order => IsOpenStatus(order.Status))
            .ToList();
    }

    /// <summary>
    /// 获取订单状态（用于测试）。
    /// </summary>
    public OrderStateUpdate? GetOrderState(string clientOrderId)
    {
        return _orders.GetValueOrDefault(clientOrderId);
    }

    /// <summary>
    /// 清除所有跟踪数据（用于测试）。
    /// </summary>
    public void Clear()
    {
        _orders.Clear();
        _openOrderCounts.Clear();
    }

    private static bool IsOpenStatus(ExecutionStatus status)
    {
        return status is ExecutionStatus.Pending or ExecutionStatus.Accepted or ExecutionStatus.PartiallyFilled;
    }
}
