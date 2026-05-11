// ============================================================================
// 策略订单状态轮询器
// ============================================================================
// 定期轮询订单状态，并将更新通过 Channel 或直接回调路由到策略。
// ============================================================================

using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Engine;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Strategy.Application.Orders;

/// <summary>
/// 策略订单状态轮询器。
/// 定期轮询订单状态并路由到策略。
/// </summary>
public sealed class StrategyOrderStatusPoller
{
    private readonly IExecutionService _executionService;
    private readonly IRiskManager _riskManager;
    private readonly StrategyOrderRegistry _registry;
    private readonly StrategyEngineOptions _options;
    private readonly ILogger<StrategyOrderStatusPoller> _logger;
    private readonly Func<string, ITradingStrategy?> _strategyResolver;
    private readonly StrategyOrderUpdateRouter? _updateRouter;

    public StrategyOrderStatusPoller(
        IExecutionService executionService,
        IRiskManager riskManager,
        StrategyOrderRegistry registry,
        IOptions<StrategyEngineOptions> options,
        ILogger<StrategyOrderStatusPoller> logger,
        Func<string, ITradingStrategy?> strategyResolver,
        StrategyOrderUpdateRouter? updateRouter = null)
    {
        _executionService = executionService ?? throw new ArgumentNullException(nameof(executionService));
        _riskManager = riskManager ?? throw new ArgumentNullException(nameof(riskManager));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _strategyResolver = strategyResolver ?? throw new ArgumentNullException(nameof(strategyResolver));
        _updateRouter = updateRouter;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(_options.OrderStatusPollIntervalSeconds);

        while (!cancellationToken.IsCancellationRequested)
        {
            var openOrders = _registry.GetOpenOrders();
            foreach (var kvp in openOrders)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var clientOrderId = kvp.Key;
                var info = kvp.Value;

                var status = await _executionService
                    .GetOrderStatusAsync(clientOrderId, cancellationToken)
                    .ConfigureAwait(false);

                if (!status.Found)
                {
                    _logger.LogWarning("Order status not found: {ClientOrderId}", clientOrderId);
                    continue;
                }

                if (status.Status == info.LastStatus && status.FilledQuantity == info.LastFilledQuantity)
                {
                    continue;
                }

                _registry.UpdateStatus(clientOrderId, status.Status, status.FilledQuantity);

                var update = new StrategyOrderUpdate(
                    info.StrategyId,
                    clientOrderId,
                    info.MarketId,
                    info.TokenId,
                    info.Outcome,
                    info.Leg,
                    info.SignalType,
                    info.Side,
                    info.OrderType,
                    info.TimeInForce,
                    info.Price,
                    status.Status,
                    status.FilledQuantity,
                    status.OriginalQuantity,
                    status.UpdatedAtUtc ?? DateTimeOffset.UtcNow);

                // Route to strategy via channel for backpressure/isolation, or fall back to direct call
                if (_updateRouter is not null)
                {
                    await _updateRouter.RouteUpdateAsync(update, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var strategy = _strategyResolver(info.StrategyId);
                    if (strategy is not null)
                    {
                        await strategy.OnOrderUpdateAsync(update, cancellationToken).ConfigureAwait(false);
                    }
                }

                await _riskManager.RecordOrderUpdateAsync(new RiskOrderUpdate
                {
                    ClientOrderId = clientOrderId,
                    Status = status.Status,
                    FilledQuantity = status.FilledQuantity,
                    OriginalQuantity = status.OriginalQuantity
                }, cancellationToken).ConfigureAwait(false);

                if (IsFinalStatus(status.Status))
                {
                    _registry.Remove(clientOrderId);
                }
            }

            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsFinalStatus(ExecutionStatus status)
    {
        return status is ExecutionStatus.Cancelled
            or ExecutionStatus.Rejected
            or ExecutionStatus.Filled
            or ExecutionStatus.Expired;
    }
}
