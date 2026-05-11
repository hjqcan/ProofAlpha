using Autotrade.Trading.Domain.Shared.Enums;
using Autotrade.Trading.Domain.Shared.ValueObjects;
using NetDevPack.Domain;

namespace Autotrade.Trading.Domain.Entities;

/// <summary>
/// 成交明细：记录订单的每笔成交。
/// </summary>
public sealed class Trade : Entity
{
    // EF Core
    private Trade()
    {
        OrderId = Guid.Empty;
        TradingAccountId = Guid.Empty;
        ClientOrderId = string.Empty;
        StrategyId = string.Empty;
        MarketId = string.Empty;
        TokenId = string.Empty;
        Outcome = OutcomeSide.Yes;
        Side = OrderSide.Buy;
        Price = Price.Zero;
        Quantity = Quantity.Zero;
        ExchangeTradeId = string.Empty;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Trade(
        Guid orderId,
        Guid tradingAccountId,
        string clientOrderId,
        string strategyId,
        string marketId,
        string tokenId,
        OutcomeSide outcome,
        OrderSide side,
        Price price,
        Quantity quantity,
        string? exchangeTradeId = null,
        decimal? fee = null,
        string? correlationId = null)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("订单 ID 不能为空", nameof(orderId));
        }

        if (tradingAccountId == Guid.Empty)
        {
            throw new ArgumentException("交易账户 ID 不能为空", nameof(tradingAccountId));
        }

        if (string.IsNullOrWhiteSpace(clientOrderId))
        {
            throw new ArgumentException("客户端订单 ID 不能为空", nameof(clientOrderId));
        }

        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(quantity);

        if (quantity.Value <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity.Value, "成交数量必须大于 0");
        }

        OrderId = orderId;
        TradingAccountId = tradingAccountId;
        ClientOrderId = clientOrderId.Trim();
        StrategyId = strategyId?.Trim() ?? string.Empty;
        MarketId = marketId?.Trim() ?? string.Empty;
        TokenId = tokenId?.Trim() ?? string.Empty;
        Outcome = outcome;
        Side = side;
        Price = price;
        Quantity = quantity;
        ExchangeTradeId = exchangeTradeId?.Trim() ?? string.Empty;
        Fee = fee ?? 0m;
        CorrelationId = correlationId?.Trim();
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 关联的订单 ID。
    /// </summary>
    public Guid OrderId { get; private set; }

    /// <summary>
    /// 交易账户 ID。
    /// </summary>
    public Guid TradingAccountId { get; private set; }

    /// <summary>
    /// 客户端订单 ID。
    /// </summary>
    public string ClientOrderId { get; private set; }

    /// <summary>
    /// 策略 ID。
    /// </summary>
    public string StrategyId { get; private set; }

    /// <summary>
    /// 市场 ID。
    /// </summary>
    public string MarketId { get; private set; }

    /// <summary>
    /// 代币 ID。
    /// </summary>
    public string TokenId { get; private set; }

    /// <summary>
    /// 结果方向（Yes/No）。
    /// </summary>
    public OutcomeSide Outcome { get; private set; }

    /// <summary>
    /// 交易方向（Buy/Sell）。
    /// </summary>
    public OrderSide Side { get; private set; }

    /// <summary>
    /// 成交价格。
    /// </summary>
    public Price Price { get; private set; }

    /// <summary>
    /// 成交数量。
    /// </summary>
    public Quantity Quantity { get; private set; }

    /// <summary>
    /// 交易所返回的成交 ID。
    /// </summary>
    public string ExchangeTradeId { get; private set; }

    /// <summary>
    /// 手续费。
    /// </summary>
    public decimal Fee { get; private set; }

    /// <summary>
    /// 关联 ID（用于跟踪）。
    /// </summary>
    public string? CorrelationId { get; private set; }

    /// <summary>
    /// 成交时间。
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>
    /// 成交金额（不含手续费）。
    /// </summary>
    public decimal Notional => Price.Value * Quantity.Value;

    /// <summary>
    /// 净成交金额（含手续费）。
    /// </summary>
    public decimal NetNotional => Side == OrderSide.Buy ? Notional + Fee : Notional - Fee;
}
