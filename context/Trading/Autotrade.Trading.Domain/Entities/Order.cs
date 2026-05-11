using Autotrade.Trading.Domain.Events;
using Autotrade.Trading.Domain.Shared.Enums;
using Autotrade.Trading.Domain.Shared.ValueObjects;
using NetDevPack.Domain;

namespace Autotrade.Trading.Domain.Entities;

/// <summary>
/// 订单实体：表达一笔对某市场（YES/NO）的买卖委托及其状态机。
/// 作为聚合根，订单状态变更时会触发领域事件。
/// </summary>
public sealed class Order : Entity, IAggregateRoot
{
    // EF Core
    private Order()
    {
        TradingAccountId = Guid.Empty;
        MarketId = string.Empty;
        Outcome = OutcomeSide.Yes;
        Side = OrderSide.Buy;
        OrderType = OrderType.Limit;
        TimeInForce = TimeInForce.Gtc;
        Price = Price.Zero;
        Quantity = Quantity.Zero;
        FilledQuantity = Quantity.Zero;
        Status = OrderStatus.Pending;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Order(
        Guid tradingAccountId,
        string marketId,
        OutcomeSide outcome,
        OrderSide side,
        OrderType orderType,
        TimeInForce timeInForce,
        Price price,
        Quantity quantity,
        DateTimeOffset? goodTilDateUtc = null,
        bool negRisk = false)
    {
        if (tradingAccountId == Guid.Empty)
        {
            throw new ArgumentException("交易账户 ID 不能为空", nameof(tradingAccountId));
        }

        if (string.IsNullOrWhiteSpace(marketId))
        {
            throw new ArgumentException("市场 ID 不能为空", nameof(marketId));
        }

        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(quantity);

        if (quantity.Value <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity.Value, "下单数量必须大于 0");
        }

        if (timeInForce == TimeInForce.Gtd)
        {
            if (goodTilDateUtc is null)
            {
                throw new ArgumentException("GTD 订单必须提供到期时间", nameof(goodTilDateUtc));
            }

            if (goodTilDateUtc <= DateTimeOffset.UtcNow)
            {
                throw new ArgumentOutOfRangeException(nameof(goodTilDateUtc), goodTilDateUtc, "GTD 到期时间必须晚于当前时间");
            }
        }

        TradingAccountId = tradingAccountId;
        MarketId = marketId.Trim();
        Outcome = outcome;
        Side = side;
        OrderType = orderType;
        TimeInForce = timeInForce;
        GoodTilDateUtc = goodTilDateUtc;
        NegRisk = negRisk;
        Price = price;
        Quantity = quantity;
        FilledQuantity = Quantity.Zero;
        Status = OrderStatus.Pending;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid TradingAccountId { get; private set; }

    public string MarketId { get; private set; }

    public OutcomeSide Outcome { get; private set; }

    public OrderSide Side { get; private set; }

    public OrderType OrderType { get; private set; }

    public TimeInForce TimeInForce { get; private set; }

    public DateTimeOffset? GoodTilDateUtc { get; private set; }

    public bool NegRisk { get; private set; }

    public Price Price { get; private set; }

    public Quantity Quantity { get; private set; }

    public Quantity FilledQuantity { get; private set; }

    public OrderStatus Status { get; private set; }

    public string? RejectionReason { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>
    /// 乐观并发控制版本号。
    /// </summary>
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    /// <summary>
    /// 策略 ID（用于审计/查询）。
    /// </summary>
    public string? StrategyId { get; private set; }

    /// <summary>
    /// 客户端订单 ID（用于关联外部系统）。
    /// </summary>
    public string? ClientOrderId { get; private set; }

    /// <summary>
    /// 交易所订单 ID（Polymarket order id）。
    /// </summary>
    public string? ExchangeOrderId { get; private set; }

    /// <summary>
    /// CLOB V2 signed order salt, persisted so uncertain submits can be safely replayed.
    /// </summary>
    public string? OrderSalt { get; private set; }

    /// <summary>
    /// CLOB V2 signed order creation timestamp in milliseconds.
    /// </summary>
    public string? OrderTimestamp { get; private set; }

    /// <summary>
    /// Token ID（Polymarket 代币标识）。
    /// </summary>
    public string? TokenId { get; private set; }

    /// <summary>
    /// 关联 ID（用于跟踪）。
    /// </summary>
    public string? CorrelationId { get; private set; }

    /// <summary>
    /// 设置策略和客户端订单 ID。
    /// </summary>
    public void SetClientInfo(string strategyId, string clientOrderId, string? tokenId = null, string? correlationId = null)
    {
        StrategyId = strategyId?.Trim();
        ClientOrderId = clientOrderId?.Trim();
        TokenId = tokenId?.Trim();
        CorrelationId = correlationId?.Trim();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void SetMarketInfo(string marketId, string? tokenId = null)
    {
        if (!string.IsNullOrWhiteSpace(marketId))
        {
            MarketId = marketId.Trim();
        }

        if (tokenId is not null)
        {
            TokenId = string.IsNullOrWhiteSpace(tokenId) ? null : tokenId.Trim();
        }

        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void SetNegRisk(bool negRisk)
    {
        NegRisk = negRisk;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 设置交易所订单 ID。
    /// </summary>
    public void SetExchangeOrderId(string? exchangeOrderId)
    {
        ExchangeOrderId = string.IsNullOrWhiteSpace(exchangeOrderId) ? null : exchangeOrderId.Trim();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void SetOrderSigningPayload(string? orderSalt, string? orderTimestamp)
    {
        OrderSalt = string.IsNullOrWhiteSpace(orderSalt) ? null : orderSalt.Trim();
        OrderTimestamp = string.IsNullOrWhiteSpace(orderTimestamp) ? null : orderTimestamp.Trim();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 标记订单已被接受（触发领域事件）。
    /// </summary>
    public void Accept(string exchangeOrderId)
    {
        if (Status != OrderStatus.Pending)
        {
            throw new InvalidOperationException($"订单状态不允许从 {Status} 切换到 {OrderStatus.Open}");
        }

        SetExchangeOrderId(exchangeOrderId);
        Status = OrderStatus.Open;
        UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddDomainEvent(new OrderAcceptedEvent(
            Id,
            ClientOrderId ?? string.Empty,
            StrategyId ?? string.Empty,
            MarketId,
            exchangeOrderId,
            CorrelationId));
    }

    /// <summary>
    /// 标记订单已开启（不触发事件，用于内部状态恢复）。
    /// </summary>
    public void MarkOpen()
    {
        if (Status != OrderStatus.Pending)
        {
            throw new InvalidOperationException($"订单状态不允许从 {Status} 切换到 {OrderStatus.Open}");
        }

        Status = OrderStatus.Open;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 设置成交数量（不触发事件，用于从 DTO 恢复状态）。
    /// </summary>
    public void SetFilledQuantity(Quantity filledQuantity)
    {
        ArgumentNullException.ThrowIfNull(filledQuantity);

        if (filledQuantity.Value < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(filledQuantity), filledQuantity.Value, "成交数量不能为负");
        }

        if (filledQuantity.Value > Quantity.Value)
        {
            throw new InvalidOperationException("成交数量累计不能超过下单数量");
        }

        FilledQuantity = filledQuantity;

        if (filledQuantity.Value == Quantity.Value)
        {
            Status = OrderStatus.Filled;
        }
        else if (filledQuantity.Value > 0m)
        {
            Status = OrderStatus.PartiallyFilled;
        }

        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 应用成交（触发领域事件）。
    /// </summary>
    public void ApplyFill(Quantity fillQuantity, decimal fillPrice)
    {
        ArgumentNullException.ThrowIfNull(fillQuantity);
        if (fillQuantity.Value <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(fillQuantity), fillQuantity.Value, "成交数量必须大于 0");
        }

        if (Status is OrderStatus.Cancelled or OrderStatus.Rejected or OrderStatus.Filled)
        {
            throw new InvalidOperationException($"订单状态为 {Status} 时不允许继续成交");
        }

        var newFilled = FilledQuantity.Value + fillQuantity.Value;
        if (newFilled > Quantity.Value)
        {
            throw new InvalidOperationException("成交数量累计不能超过下单数量");
        }

        FilledQuantity = new Quantity(newFilled);
        var isPartial = newFilled < Quantity.Value;

        Status = isPartial ? OrderStatus.PartiallyFilled : OrderStatus.Filled;
        UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddDomainEvent(new OrderFilledEvent(
            Id,
            ClientOrderId ?? string.Empty,
            StrategyId ?? string.Empty,
            MarketId,
            fillQuantity.Value,
            fillPrice,
            isPartial,
            CorrelationId));
    }

    /// <summary>
    /// 取消订单（触发领域事件）。
    /// </summary>
    public void Cancel()
    {
        if (Status is OrderStatus.Filled or OrderStatus.Rejected)
        {
            throw new InvalidOperationException($"订单状态为 {Status} 时不允许撤单");
        }

        Status = OrderStatus.Cancelled;
        UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddDomainEvent(new OrderCancelledEvent(
            Id,
            ClientOrderId ?? string.Empty,
            StrategyId ?? string.Empty,
            MarketId,
            CorrelationId));
    }

    /// <summary>
    /// 订单过期（触发领域事件）。
    /// </summary>
    public void Expire()
    {
        if (Status is OrderStatus.Filled or OrderStatus.Rejected or OrderStatus.Cancelled or OrderStatus.Expired)
        {
            throw new InvalidOperationException($"订单状态为 {Status} 时不允许过期");
        }

        Status = OrderStatus.Expired;
        UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddDomainEvent(new OrderExpiredEvent(
            Id,
            ClientOrderId ?? string.Empty,
            StrategyId ?? string.Empty,
            MarketId,
            CorrelationId));
    }

    /// <summary>
    /// 拒绝订单（触发领域事件）。
    /// </summary>
    public void Reject(string reason)
    {
        if (Status is OrderStatus.Filled or OrderStatus.Cancelled)
        {
            throw new InvalidOperationException($"订单状态为 {Status} 时不允许拒绝");
        }

        RejectionReason = string.IsNullOrWhiteSpace(reason) ? "未知原因" : reason.Trim();
        Status = OrderStatus.Rejected;
        UpdatedAtUtc = DateTimeOffset.UtcNow;

        AddDomainEvent(new OrderRejectedEvent(
            Id,
            ClientOrderId ?? string.Empty,
            StrategyId ?? string.Empty,
            MarketId,
            RejectionReason,
            CorrelationId));
    }
}

