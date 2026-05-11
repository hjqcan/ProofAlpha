using Autotrade.Application.RunSessions;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.RunSessions;
using Autotrade.Trading.Domain.Events;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetDevPack.Messaging;

namespace Autotrade.Trading.Application.EventHandlers;

/// <summary>
/// 订单取消事件处理器。
/// 写入 OrderEvent 审计日志。
/// </summary>
public sealed class OrderCancelledEventHandler : IDomainEventHandler<OrderCancelledEvent>
{
    private readonly IOrderEventRepository _orderEventRepository;
    private readonly ILogger<OrderCancelledEventHandler> _logger;
    private readonly ExecutionOptions _executionOptions;
    private readonly IRunSessionAccessor? _runSessionAccessor;

    public OrderCancelledEventHandler(
        IOrderEventRepository orderEventRepository,
        ILogger<OrderCancelledEventHandler> logger,
        IOptions<ExecutionOptions>? executionOptions = null,
        IRunSessionAccessor? runSessionAccessor = null)
    {
        _orderEventRepository = orderEventRepository ?? throw new ArgumentNullException(nameof(orderEventRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _executionOptions = executionOptions?.Value ?? new ExecutionOptions();
        _runSessionAccessor = runSessionAccessor;
    }

    public async Task Handle(OrderCancelledEvent @event)
    {
        _logger.LogDebug(
            "Handling OrderCancelledEvent: OrderId={OrderId}, ClientOrderId={ClientOrderId}",
            @event.AggregateId,
            @event.ClientOrderId);

        try
        {
            await _orderEventRepository.AddAsync(new OrderEventDto(
                Id: Guid.NewGuid(),
                OrderId: @event.AggregateId,
                ClientOrderId: @event.ClientOrderId,
                StrategyId: @event.StrategyId,
                MarketId: @event.MarketId,
                EventType: OrderEventType.Cancelled,
                Status: OrderStatus.Cancelled,
                Message: "Order cancelled",
                ContextJson: null,
                CorrelationId: @event.CorrelationId,
                CreatedAtUtc: @event.Timestamp,
                RunSessionId: await TradingRunSessionResolver
                    .ResolvePaperRunSessionIdAsync(_runSessionAccessor, _executionOptions, _logger)
                    .ConfigureAwait(false))).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log OrderCancelledEvent: OrderId={OrderId}", @event.AggregateId);
        }
    }
}
