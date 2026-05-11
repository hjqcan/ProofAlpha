using System.Text.Json;
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
/// 订单成交事件处理器。
/// 写入 OrderEvent 审计日志。
/// </summary>
public sealed class OrderFilledEventHandler : IDomainEventHandler<OrderFilledEvent>
{
    private readonly IOrderEventRepository _orderEventRepository;
    private readonly ILogger<OrderFilledEventHandler> _logger;
    private readonly ExecutionOptions _executionOptions;
    private readonly IRunSessionAccessor? _runSessionAccessor;

    public OrderFilledEventHandler(
        IOrderEventRepository orderEventRepository,
        ILogger<OrderFilledEventHandler> logger,
        IOptions<ExecutionOptions>? executionOptions = null,
        IRunSessionAccessor? runSessionAccessor = null)
    {
        _orderEventRepository = orderEventRepository ?? throw new ArgumentNullException(nameof(orderEventRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _executionOptions = executionOptions?.Value ?? new ExecutionOptions();
        _runSessionAccessor = runSessionAccessor;
    }

    public async Task Handle(OrderFilledEvent @event)
    {
        _logger.LogDebug(
            "Handling OrderFilledEvent: OrderId={OrderId}, FilledQuantity={FilledQuantity}, IsPartial={IsPartial}",
            @event.AggregateId,
            @event.FilledQuantity,
            @event.IsPartial);

        try
        {
            var eventType = @event.IsPartial ? OrderEventType.PartiallyFilled : OrderEventType.Filled;
            var status = @event.IsPartial ? OrderStatus.PartiallyFilled : OrderStatus.Filled;

            var contextJson = JsonSerializer.Serialize(new
            {
                FillPrice = @event.FillPrice,
                FilledQuantity = @event.FilledQuantity
            });

            await _orderEventRepository.AddAsync(new OrderEventDto(
                Id: Guid.NewGuid(),
                OrderId: @event.AggregateId,
                ClientOrderId: @event.ClientOrderId,
                StrategyId: @event.StrategyId,
                MarketId: @event.MarketId,
                EventType: eventType,
                Status: status,
                Message: $"Filled {@event.FilledQuantity} @ {@event.FillPrice:F4}",
                ContextJson: contextJson,
                CorrelationId: @event.CorrelationId,
                CreatedAtUtc: @event.Timestamp,
                RunSessionId: await TradingRunSessionResolver
                    .ResolvePaperRunSessionIdAsync(_runSessionAccessor, _executionOptions, _logger)
                    .ConfigureAwait(false))).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log OrderFilledEvent: OrderId={OrderId}", @event.AggregateId);
        }
    }
}
