using Autotrade.Domain.Abstractions.EventBus;
using Autotrade.Trading.Domain.Shared.Enums;
using NetDevPack.Messaging;

namespace Autotrade.Trading.Domain.Events;

/// <summary>
/// 成交执行领域事件。
/// 当订单产生实际成交时触发，用于记录成交明细。
/// </summary>
public sealed class TradeExecutedEvent : DomainEvent, IIntegrationEvent
{
    public TradeExecutedEvent(
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
        string? correlationId = null) : base(orderId)
    {
        TradingAccountId = tradingAccountId;
        ClientOrderId = clientOrderId ?? throw new ArgumentNullException(nameof(clientOrderId));
        StrategyId = strategyId ?? string.Empty;
        MarketId = marketId ?? throw new ArgumentNullException(nameof(marketId));
        TokenId = tokenId ?? throw new ArgumentNullException(nameof(tokenId));
        Outcome = outcome;
        Side = side;
        Price = price;
        Quantity = quantity;
        ExchangeTradeId = exchangeTradeId ?? string.Empty;
        Fee = fee;
        CorrelationId = correlationId;
    }

    public Guid TradingAccountId { get; }
    public string ClientOrderId { get; }
    public string StrategyId { get; }
    public string MarketId { get; }
    public string TokenId { get; }
    public OutcomeSide Outcome { get; }
    public OrderSide Side { get; }
    public decimal Price { get; }
    public decimal Quantity { get; }
    public string ExchangeTradeId { get; }
    public decimal Fee { get; }
    public string? CorrelationId { get; }

    /// <summary>
    /// 成交金额（不含手续费）。
    /// </summary>
    public decimal Notional => Price * Quantity;

    // IIntegrationEvent
    public string EventName => "Trading.Trade.Executed";
    public string Version => "v1";
}
