using Autotrade.Domain.Abstractions.EventBus;
using NetDevPack.Messaging;

namespace Autotrade.Trading.Domain.Events;

/// <summary>
/// 订单已取消领域事件。
/// 当订单被取消时触发。
/// </summary>
public sealed class OrderCancelledEvent : DomainEvent, IIntegrationEvent
{
    public OrderCancelledEvent(
        Guid orderId,
        string clientOrderId,
        string strategyId,
        string marketId,
        string? correlationId = null) : base(orderId)
    {
        ClientOrderId = clientOrderId ?? throw new ArgumentNullException(nameof(clientOrderId));
        StrategyId = strategyId ?? string.Empty;
        MarketId = marketId ?? throw new ArgumentNullException(nameof(marketId));
        CorrelationId = correlationId;
    }

    public string ClientOrderId { get; }
    public string StrategyId { get; }
    public string MarketId { get; }
    public string? CorrelationId { get; }

    // IIntegrationEvent
    public string EventName => "Trading.Order.Cancelled";
    public string Version => "v1";
}
