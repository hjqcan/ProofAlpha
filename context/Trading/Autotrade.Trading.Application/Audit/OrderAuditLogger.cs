using Autotrade.Application.RunSessions;
using Autotrade.Trading.Application.Contract.Audit;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.RunSessions;
using Autotrade.Trading.Domain.Events;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetDevPack.Messaging;

namespace Autotrade.Trading.Application.Audit;

/// <summary>
/// 订单审计日志记录器实现。
/// </summary>
public sealed class OrderAuditLogger : IOrderAuditLogger
{
    private readonly IOrderEventRepository _orderEventRepository;
    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private readonly ILogger<OrderAuditLogger> _logger;
    private readonly ExecutionOptions _executionOptions;
    private readonly IRunSessionAccessor? _runSessionAccessor;

    public OrderAuditLogger(
        IOrderEventRepository orderEventRepository,
        IDomainEventDispatcher domainEventDispatcher,
        ILogger<OrderAuditLogger> logger,
        IOptions<ExecutionOptions>? executionOptions = null,
        IRunSessionAccessor? runSessionAccessor = null)
    {
        _orderEventRepository = orderEventRepository ?? throw new ArgumentNullException(nameof(orderEventRepository));
        _domainEventDispatcher = domainEventDispatcher ?? throw new ArgumentNullException(nameof(domainEventDispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _executionOptions = executionOptions?.Value ?? new ExecutionOptions();
        _runSessionAccessor = runSessionAccessor;
    }

    /// <inheritdoc />
    public async Task LogOrderCreatedAsync(
        Guid orderId,
        string clientOrderId,
        string strategyId,
        string marketId,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        await LogEventAsync(
            orderId,
            clientOrderId,
            strategyId,
            marketId,
            OrderEventType.Created,
            OrderStatus.Pending,
            "Order created",
            null,
            correlationId,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task LogOrderSubmittedAsync(
        Guid orderId,
        string clientOrderId,
        string strategyId,
        string marketId,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        await LogEventAsync(
            orderId,
            clientOrderId,
            strategyId,
            marketId,
            OrderEventType.Submitted,
            OrderStatus.Pending,
            "Order submitted",
            null,
            correlationId,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task LogOrderAcceptedAsync(
        Guid orderId,
        string clientOrderId,
        string strategyId,
        string marketId,
        string? exchangeOrderId,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _domainEventDispatcher.DispatchAsync(new[]
                {
                    new OrderAcceptedEvent(
                        orderId,
                        clientOrderId,
                        strategyId,
                        marketId,
                        exchangeOrderId ?? string.Empty,
                        correlationId)
                })
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch OrderAcceptedEvent: OrderId={OrderId}", orderId);
        }
    }

    /// <inheritdoc />
    public async Task LogOrderRejectedAsync(
        Guid orderId,
        string clientOrderId,
        string strategyId,
        string marketId,
        string errorCode,
        string errorMessage,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var reason = string.IsNullOrWhiteSpace(errorCode)
                ? errorMessage
                : $"{errorCode}: {errorMessage}";

            await _domainEventDispatcher.DispatchAsync(new[]
                {
                    new OrderRejectedEvent(
                        orderId,
                        clientOrderId,
                        strategyId,
                        marketId,
                        reason,
                        correlationId)
                })
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch OrderRejectedEvent: OrderId={OrderId}", orderId);
        }
    }

    /// <inheritdoc />
    public async Task LogOrderFilledAsync(
        Guid orderId,
        string clientOrderId,
        string strategyId,
        string marketId,
        decimal filledQuantity,
        decimal fillPrice,
        bool isPartial,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _domainEventDispatcher.DispatchAsync(new[]
                {
                    new OrderFilledEvent(
                        orderId,
                        clientOrderId,
                        strategyId,
                        marketId,
                        filledQuantity,
                        fillPrice,
                        isPartial,
                        correlationId)
                })
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch OrderFilledEvent: OrderId={OrderId}", orderId);
        }
    }

    /// <inheritdoc />
    public async Task LogOrderCancelledAsync(
        Guid orderId,
        string clientOrderId,
        string strategyId,
        string marketId,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _domainEventDispatcher.DispatchAsync(new[]
                {
                    new OrderCancelledEvent(
                        orderId,
                        clientOrderId,
                        strategyId,
                        marketId,
                        correlationId)
                })
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch OrderCancelledEvent: OrderId={OrderId}", orderId);
        }
    }

    /// <inheritdoc />
    public async Task LogOrderExpiredAsync(
        Guid orderId,
        string clientOrderId,
        string strategyId,
        string marketId,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _domainEventDispatcher.DispatchAsync(new[]
                {
                    new OrderExpiredEvent(
                        orderId,
                        clientOrderId,
                        strategyId,
                        marketId,
                        correlationId)
                })
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch OrderExpiredEvent: OrderId={OrderId}", orderId);
        }
    }

    /// <inheritdoc />
    public async Task LogTradeAsync(
        Guid orderId,
        Guid tradingAccountId,
        string clientOrderId,
        string strategyId,
        string marketId,
        string tokenId,
        OutcomeSide outcome,
        OrderSide side,
        decimal price,
        decimal quantity,
        string exchangeTradeId,
        decimal fee,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _domainEventDispatcher.DispatchAsync(new[]
                {
                    new TradeExecutedEvent(
                        orderId,
                        tradingAccountId,
                        clientOrderId,
                        strategyId,
                        marketId,
                        tokenId,
                        outcome,
                        side,
                        price,
                        quantity,
                        exchangeTradeId,
                        fee,
                        correlationId)
                })
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch TradeExecutedEvent: OrderId={OrderId}", orderId);
        }
    }

    private async Task LogEventAsync(
        Guid orderId,
        string clientOrderId,
        string strategyId,
        string marketId,
        OrderEventType eventType,
        OrderStatus status,
        string message,
        string? contextJson,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var eventDto = new OrderEventDto(
                Guid.NewGuid(),
                orderId,
                clientOrderId,
                strategyId,
                marketId,
                eventType,
                status,
                message,
                contextJson,
                correlationId,
                DateTimeOffset.UtcNow,
                await TradingRunSessionResolver
                    .ResolvePaperRunSessionIdAsync(_runSessionAccessor, _executionOptions, _logger, cancellationToken)
                    .ConfigureAwait(false));

            await _orderEventRepository.AddAsync(eventDto, cancellationToken).ConfigureAwait(false);

            _logger.LogDebug(
                "Order event logged: OrderId={OrderId}, ClientOrderId={ClientOrderId}, EventType={EventType}, Status={Status}",
                orderId, clientOrderId, eventType, status);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log order event for order {OrderId}", orderId);
        }
    }

}
