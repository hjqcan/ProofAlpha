using Autotrade.Domain.Abstractions.EventBus;
using Autotrade.Trading.Domain.Shared.Enums;
using NetDevPack.Messaging;

namespace Autotrade.Trading.Domain.Events;

/// <summary>
/// 订单已成交领域事件。
/// 当订单部分或全部成交时触发。
/// </summary>
public sealed class OrderFilledEvent : DomainEvent, IIntegrationEvent
{
    public OrderFilledEvent(
        Guid orderId,
        string clientOrderId,
        string strategyId,
        string marketId,
        decimal filledQuantity,
        decimal fillPrice,
        bool isPartial,
        string? correlationId = null) : base(orderId)
    {
        ClientOrderId = clientOrderId ?? throw new ArgumentNullException(nameof(clientOrderId));
        StrategyId = strategyId ?? string.Empty;
        MarketId = marketId ?? throw new ArgumentNullException(nameof(marketId));
        FilledQuantity = filledQuantity;
        FillPrice = fillPrice;
        IsPartial = isPartial;
        CorrelationId = correlationId;
    }

    public string ClientOrderId { get; }
    public string StrategyId { get; }
    public string MarketId { get; }
    public decimal FilledQuantity { get; }
    public decimal FillPrice { get; }
    public bool IsPartial { get; }
    public string? CorrelationId { get; }

    // IIntegrationEvent
    public string EventName => IsPartial ? "Trading.Order.PartiallyFilled" : "Trading.Order.Filled";
    public string Version => "v1";
}
