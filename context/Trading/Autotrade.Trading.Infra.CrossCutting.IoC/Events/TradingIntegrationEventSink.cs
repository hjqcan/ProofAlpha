using Autotrade.Trading.Domain.Shared.IntegrationEvents;
using DotNetCore.CAP;
using Microsoft.Extensions.Logging;

namespace Autotrade.Trading.Infra.CrossCutting.IoC.Events;

/// <summary>
/// Drains trading integration events when the in-memory CAP transport is used locally.
/// </summary>
public sealed class TradingIntegrationEventSink
{
    private const string ConsumerGroup = "autotrade.trading.inmemory-sink";

    private readonly ILogger<TradingIntegrationEventSink> _logger;

    public TradingIntegrationEventSink(ILogger<TradingIntegrationEventSink> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [CapSubscribe(OrderAcceptedIntegrationEventDto.EventName, false, Group = ConsumerGroup)]
    public Task HandleAsync(OrderAcceptedIntegrationEventDto message)
    {
        LogDrained(OrderAcceptedIntegrationEventDto.EventName, message.OrderId, message.StrategyId);
        return Task.CompletedTask;
    }

    [CapSubscribe(OrderCancelledIntegrationEventDto.EventName, false, Group = ConsumerGroup)]
    public Task HandleAsync(OrderCancelledIntegrationEventDto message)
    {
        LogDrained(OrderCancelledIntegrationEventDto.EventName, message.OrderId, message.StrategyId);
        return Task.CompletedTask;
    }

    [CapSubscribe(OrderExpiredIntegrationEventDto.EventName, false, Group = ConsumerGroup)]
    public Task HandleAsync(OrderExpiredIntegrationEventDto message)
    {
        LogDrained(OrderExpiredIntegrationEventDto.EventName, message.OrderId, message.StrategyId);
        return Task.CompletedTask;
    }

    [CapSubscribe(OrderFilledIntegrationEventDto.FilledEventName, false, Group = ConsumerGroup)]
    public Task HandleFilledAsync(OrderFilledIntegrationEventDto message)
    {
        LogDrained(OrderFilledIntegrationEventDto.FilledEventName, message.OrderId, message.StrategyId);
        return Task.CompletedTask;
    }

    [CapSubscribe(OrderFilledIntegrationEventDto.PartiallyFilledEventName, false, Group = ConsumerGroup)]
    public Task HandlePartiallyFilledAsync(OrderFilledIntegrationEventDto message)
    {
        LogDrained(OrderFilledIntegrationEventDto.PartiallyFilledEventName, message.OrderId, message.StrategyId);
        return Task.CompletedTask;
    }

    [CapSubscribe(OrderRejectedIntegrationEventDto.EventName, false, Group = ConsumerGroup)]
    public Task HandleAsync(OrderRejectedIntegrationEventDto message)
    {
        LogDrained(OrderRejectedIntegrationEventDto.EventName, message.OrderId, message.StrategyId);
        return Task.CompletedTask;
    }

    [CapSubscribe(TradeExecutedIntegrationEventDto.EventName, false, Group = ConsumerGroup)]
    public Task HandleAsync(TradeExecutedIntegrationEventDto message)
    {
        LogDrained(TradeExecutedIntegrationEventDto.EventName, message.OrderId, message.StrategyId);
        return Task.CompletedTask;
    }

    private void LogDrained(string eventName, Guid orderId, string strategyId)
    {
        _logger.LogTrace(
            "Drained local trading integration event {EventName}: OrderId={OrderId}, StrategyId={StrategyId}",
            eventName,
            orderId,
            strategyId);
    }
}
