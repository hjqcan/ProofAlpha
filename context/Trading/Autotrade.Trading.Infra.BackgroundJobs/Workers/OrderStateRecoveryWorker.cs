using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.Execution;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Autotrade.Trading.Application.Contract.Execution;

namespace Autotrade.Trading.Infra.BackgroundJobs.Workers;

/// <summary>
/// 启动时从数据库恢复未完成订单的内存映射，保证重启后仍可撤单、查单与对账。
/// </summary>
public sealed class OrderStateRecoveryWorker : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly IOrderStateTracker _stateTracker;
    private readonly ExecutionOptions _options;
    private readonly ILogger<OrderStateRecoveryWorker> _logger;

    public OrderStateRecoveryWorker(
        IServiceScopeFactory scopeFactory,
        IIdempotencyStore idempotencyStore,
        IOrderStateTracker stateTracker,
        IOptions<ExecutionOptions> options,
        ILogger<OrderStateRecoveryWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _idempotencyStore = idempotencyStore ?? throw new ArgumentNullException(nameof(idempotencyStore));
        _stateTracker = stateTracker ?? throw new ArgumentNullException(nameof(stateTracker));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.Mode != ExecutionMode.Live)
        {
            _logger.LogInformation("Skipping order state recovery for execution mode {Mode}", _options.Mode);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var openOrders = await repository.GetOpenOrdersAsync(cancellationToken).ConfigureAwait(false);

        var restored = 0;
        foreach (var order in openOrders)
        {
            if (string.IsNullOrWhiteSpace(order.ClientOrderId))
            {
                continue;
            }

            await _stateTracker.OnOrderStateChangedAsync(new OrderStateUpdate
            {
                ClientOrderId = order.ClientOrderId,
                ExchangeOrderId = order.ExchangeOrderId ?? string.Empty,
                MarketId = order.MarketId,
                TokenId = order.TokenId,
                Status = ToExecutionStatus(order.Status),
                OriginalQuantity = order.Quantity,
                FilledQuantity = order.FilledQuantity,
                UpdatedAtUtc = order.UpdatedAtUtc
            }, cancellationToken).ConfigureAwait(false);

            await _idempotencyStore.SeedAsync(new OrderTrackingEntry
            {
                ClientOrderId = order.ClientOrderId,
                ExchangeOrderId = order.ExchangeOrderId,
                MarketId = order.MarketId,
                TokenId = order.TokenId,
                StrategyId = order.StrategyId,
                CorrelationId = order.CorrelationId,
                OrderSalt = order.OrderSalt,
                OrderTimestamp = order.OrderTimestamp,
                IsUncertainSubmit = order.Status == OrderStatus.Pending &&
                    string.IsNullOrWhiteSpace(order.ExchangeOrderId),
                RequestHash = ExecutionRequestHasher.Compute(order),
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(3600, _options.IdempotencyTtlSeconds))
            }, cancellationToken).ConfigureAwait(false);

            restored++;
        }

        _logger.LogInformation("恢复未完成订单状态完成: Count={Count}", restored);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static ExecutionStatus ToExecutionStatus(OrderStatus status) => status switch
    {
        OrderStatus.Pending => ExecutionStatus.Pending,
        OrderStatus.Open => ExecutionStatus.Accepted,
        OrderStatus.PartiallyFilled => ExecutionStatus.PartiallyFilled,
        OrderStatus.Filled => ExecutionStatus.Filled,
        OrderStatus.Cancelled => ExecutionStatus.Cancelled,
        OrderStatus.Rejected => ExecutionStatus.Rejected,
        OrderStatus.Expired => ExecutionStatus.Expired,
        _ => ExecutionStatus.Pending
    };
}
