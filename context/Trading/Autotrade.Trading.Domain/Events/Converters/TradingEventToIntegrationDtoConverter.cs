using Autotrade.Trading.Domain.Shared.IntegrationEvents;

namespace Autotrade.Trading.Domain.Events.Converters;

public static class TradingEventToIntegrationDtoConverter
{
    public static OrderAcceptedIntegrationEventDto ToIntegrationDto(this OrderAcceptedEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return new OrderAcceptedIntegrationEventDto(
            domainEvent.AggregateId,
            domainEvent.ClientOrderId,
            domainEvent.StrategyId,
            domainEvent.MarketId,
            domainEvent.ExchangeOrderId,
            domainEvent.CorrelationId,
            ToUtc(domainEvent.Timestamp));
    }

    public static OrderCancelledIntegrationEventDto ToIntegrationDto(this OrderCancelledEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return new OrderCancelledIntegrationEventDto(
            domainEvent.AggregateId,
            domainEvent.ClientOrderId,
            domainEvent.StrategyId,
            domainEvent.MarketId,
            domainEvent.CorrelationId,
            ToUtc(domainEvent.Timestamp));
    }

    public static OrderExpiredIntegrationEventDto ToIntegrationDto(this OrderExpiredEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return new OrderExpiredIntegrationEventDto(
            domainEvent.AggregateId,
            domainEvent.ClientOrderId,
            domainEvent.StrategyId,
            domainEvent.MarketId,
            domainEvent.CorrelationId,
            ToUtc(domainEvent.Timestamp));
    }

    public static OrderFilledIntegrationEventDto ToIntegrationDto(this OrderFilledEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return new OrderFilledIntegrationEventDto(
            domainEvent.AggregateId,
            domainEvent.ClientOrderId,
            domainEvent.StrategyId,
            domainEvent.MarketId,
            domainEvent.FilledQuantity,
            domainEvent.FillPrice,
            domainEvent.IsPartial,
            domainEvent.CorrelationId,
            ToUtc(domainEvent.Timestamp));
    }

    public static OrderRejectedIntegrationEventDto ToIntegrationDto(this OrderRejectedEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return new OrderRejectedIntegrationEventDto(
            domainEvent.AggregateId,
            domainEvent.ClientOrderId,
            domainEvent.StrategyId,
            domainEvent.MarketId,
            domainEvent.RejectReason,
            domainEvent.CorrelationId,
            ToUtc(domainEvent.Timestamp));
    }

    public static TradeExecutedIntegrationEventDto ToIntegrationDto(this TradeExecutedEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return new TradeExecutedIntegrationEventDto(
            domainEvent.AggregateId,
            domainEvent.TradingAccountId,
            domainEvent.ClientOrderId,
            domainEvent.StrategyId,
            domainEvent.MarketId,
            domainEvent.TokenId,
            domainEvent.Outcome.ToString(),
            domainEvent.Side.ToString(),
            domainEvent.Price,
            domainEvent.Quantity,
            domainEvent.Notional,
            domainEvent.ExchangeTradeId,
            domainEvent.Fee,
            domainEvent.CorrelationId,
            ToUtc(domainEvent.Timestamp));
    }

    private static DateTimeOffset ToUtc(DateTime timestamp)
    {
        var timestampWithKind = timestamp.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(timestamp, DateTimeKind.Local)
            : timestamp;

        return new DateTimeOffset(timestampWithKind).ToUniversalTime();
    }
}
