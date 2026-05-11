using Autotrade.Domain.Abstractions.EventBus;
using NetDevPack.Messaging;

namespace Autotrade.Trading.Domain.Events.Converters;

public sealed class TradingIntegrationDtoConverter : IIntegrationDtoConverter
{
    public bool CanConvert(Event domainEvent)
    {
        return domainEvent is OrderAcceptedEvent
            or OrderCancelledEvent
            or OrderExpiredEvent
            or OrderFilledEvent
            or OrderRejectedEvent
            or TradeExecutedEvent;
    }

    public object Convert(Event domainEvent)
    {
        return domainEvent switch
        {
            OrderAcceptedEvent orderAccepted => orderAccepted.ToIntegrationDto(),
            OrderCancelledEvent orderCancelled => orderCancelled.ToIntegrationDto(),
            OrderExpiredEvent orderExpired => orderExpired.ToIntegrationDto(),
            OrderFilledEvent orderFilled => orderFilled.ToIntegrationDto(),
            OrderRejectedEvent orderRejected => orderRejected.ToIntegrationDto(),
            TradeExecutedEvent tradeExecuted => tradeExecuted.ToIntegrationDto(),
            _ => throw new NotSupportedException($"Unsupported trading integration event: {domainEvent.GetType().Name}")
        };
    }
}
