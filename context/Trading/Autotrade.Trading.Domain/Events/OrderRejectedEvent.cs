using Autotrade.Domain.Abstractions.EventBus;
using NetDevPack.Messaging;

namespace Autotrade.Trading.Domain.Events;

/// <summary>
/// 订单已拒绝领域事件。
/// 当订单被交易所或风控拒绝时触发。
/// </summary>
public sealed class OrderRejectedEvent : DomainEvent, IIntegrationEvent
{
    public OrderRejectedEvent(
        Guid orderId,
        string clientOrderId,
        string strategyId,
        string marketId,
        string rejectReason,
        string? correlationId = null) : base(orderId)
    {
        ClientOrderId = clientOrderId ?? throw new ArgumentNullException(nameof(clientOrderId));
        StrategyId = strategyId ?? string.Empty;
        MarketId = marketId ?? throw new ArgumentNullException(nameof(marketId));
        RejectReason = rejectReason ?? string.Empty;
        CorrelationId = correlationId;
    }

    public string ClientOrderId { get; }
    public string StrategyId { get; }
    public string MarketId { get; }
    public string RejectReason { get; }
    public string? CorrelationId { get; }

    // IIntegrationEvent
    public string EventName => "Trading.Order.Rejected";
    public string Version => "v1";
}
