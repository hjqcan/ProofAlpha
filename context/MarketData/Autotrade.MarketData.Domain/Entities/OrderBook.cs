using Autotrade.MarketData.Domain.Shared.ValueObjects;
using NetDevPack.Domain;

namespace Autotrade.MarketData.Domain.Entities;

/// <summary>
/// 订单簿（领域实体，MVP 阶段仅建模“最优买卖”与更新时间）。
/// 后续在 MarketData/WebSocket 任务中扩展为“快照 + 增量”深度簿。
/// </summary>
public sealed class OrderBook : Entity
{
    // EF Core
    private OrderBook()
    {
        MarketId = string.Empty;
        LastUpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public OrderBook(string marketId)
    {
        if (string.IsNullOrWhiteSpace(marketId))
        {
            throw new ArgumentException("市场 ID 不能为空", nameof(marketId));
        }

        MarketId = marketId.Trim();
        LastUpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public string MarketId { get; private set; }

    public Price? BestBidPrice { get; private set; }

    public Quantity? BestBidSize { get; private set; }

    public Price? BestAskPrice { get; private set; }

    public Quantity? BestAskSize { get; private set; }

    public DateTimeOffset LastUpdatedAtUtc { get; private set; }

    public void UpdateTopOfBook(
        Price? bestBidPrice,
        Quantity? bestBidSize,
        Price? bestAskPrice,
        Quantity? bestAskSize,
        DateTimeOffset? updatedAtUtc = null)
    {
        BestBidPrice = bestBidPrice;
        BestBidSize = bestBidSize;
        BestAskPrice = bestAskPrice;
        BestAskSize = bestAskSize;
        LastUpdatedAtUtc = updatedAtUtc ?? DateTimeOffset.UtcNow;
    }
}

