using Autotrade.Domain.Abstractions.EventBus;
using NetDevPack.Messaging;

namespace Autotrade.Trading.Domain.Events;

/// <summary>
/// 订单已接受领域事件。
/// 当交易所确认接受订单时触发。
/// </summary>
public sealed class OrderAcceptedEvent : DomainEvent, IIntegrationEvent
{
    public OrderAcceptedEvent(
        Guid orderId,
        string clientOrderId,
        string strategyId,
        string marketId,
        string exchangeOrderId,
        string? correlationId = null) : base(orderId)
    {
        ClientOrderId = clientOrderId ?? throw new ArgumentNullException(nameof(clientOrderId));
        StrategyId = strategyId ?? string.Empty;
        MarketId = marketId ?? throw new ArgumentNullException(nameof(marketId));
        ExchangeOrderId = exchangeOrderId ?? throw new ArgumentNullException(nameof(exchangeOrderId));
        CorrelationId = correlationId;
    }

    public string ClientOrderId { get; }
    public string StrategyId { get; }
    public string MarketId { get; }
    public string ExchangeOrderId { get; }
    public string? CorrelationId { get; }

    // IIntegrationEvent
    public string EventName => "Trading.Order.Accepted";
    public string Version => "v1";
}
