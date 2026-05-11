using Autotrade.Trading.Domain.Shared.Enums;
using Autotrade.Trading.Domain.Shared.ValueObjects;
using NetDevPack.Domain;

namespace Autotrade.Trading.Domain.Entities;

/// <summary>
/// 持仓实体：某市场某结果侧（YES/NO）的仓位快照。
/// </summary>
public sealed class Position : Entity
{
    // EF Core
    private Position()
    {
        TradingAccountId = Guid.Empty;
        MarketId = string.Empty;
        Outcome = OutcomeSide.Yes;
        Quantity = Quantity.Zero;
        AverageCost = Price.Zero;
        RealizedPnl = 0m;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Position(Guid tradingAccountId, string marketId, OutcomeSide outcome)
    {
        if (tradingAccountId == Guid.Empty)
        {
            throw new ArgumentException("交易账户 ID 不能为空", nameof(tradingAccountId));
        }

        if (string.IsNullOrWhiteSpace(marketId))
        {
            throw new ArgumentException("市场 ID 不能为空", nameof(marketId));
        }

        TradingAccountId = tradingAccountId;
        MarketId = marketId.Trim();
        Outcome = outcome;
        Quantity = Quantity.Zero;
        AverageCost = Price.Zero;
        RealizedPnl = 0m;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid TradingAccountId { get; private set; }

    public string MarketId { get; private set; }

    public OutcomeSide Outcome { get; private set; }

    public Quantity Quantity { get; private set; }

    public Price AverageCost { get; private set; }

    /// <summary>
    /// 已实现盈亏（以 USDC 计价的简化模型）。
    /// </summary>
    public decimal RealizedPnl { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void ApplyBuy(Quantity buyQuantity, Price buyPrice)
    {
        ArgumentNullException.ThrowIfNull(buyQuantity);
        ArgumentNullException.ThrowIfNull(buyPrice);

        if (buyQuantity.Value <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(buyQuantity), buyQuantity.Value, "买入数量必须大于 0");
        }

        var oldQty = Quantity.Value;
        var newQty = oldQty + buyQuantity.Value;

        var newAvg = newQty == 0m
            ? 0m
            : ((AverageCost.Value * oldQty) + (buyPrice.Value * buyQuantity.Value)) / newQty;

        Quantity = new Quantity(newQty);
        AverageCost = new Price(newAvg);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void ApplySell(Quantity sellQuantity, Price sellPrice)
    {
        ArgumentNullException.ThrowIfNull(sellQuantity);
        ArgumentNullException.ThrowIfNull(sellPrice);

        if (sellQuantity.Value <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(sellQuantity), sellQuantity.Value, "卖出数量必须大于 0");
        }

        if (sellQuantity.Value > Quantity.Value)
        {
            throw new InvalidOperationException("卖出数量不能超过持仓数量");
        }

        // 简化：按均价计算已实现盈亏
        RealizedPnl += (sellPrice.Value - AverageCost.Value) * sellQuantity.Value;

        var newQty = Quantity.Value - sellQuantity.Value;
        Quantity = new Quantity(newQty);

        if (newQty == 0m)
        {
            AverageCost = Price.Zero;
        }

        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}

