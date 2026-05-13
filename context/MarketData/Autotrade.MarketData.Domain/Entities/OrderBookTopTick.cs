using NetDevPack.Domain;

namespace Autotrade.MarketData.Domain.Entities;

public sealed class OrderBookTopTick : Entity, IAggregateRoot
{
    private OrderBookTopTick()
    {
        MarketId = string.Empty;
        TokenId = string.Empty;
        SourceName = string.Empty;
        RawJson = "{}";
        TimestampUtc = DateTimeOffset.UtcNow;
        TimestampUnixMilliseconds = TimestampUtc.ToUnixTimeMilliseconds();
        CreatedAtUtc = TimestampUtc;
    }

    public OrderBookTopTick(
        string marketId,
        string tokenId,
        DateTimeOffset timestampUtc,
        decimal? bestBidPrice,
        decimal? bestBidSize,
        decimal? bestAskPrice,
        decimal? bestAskSize,
        string sourceName,
        string? sourceSequence,
        string rawJson,
        DateTimeOffset createdAtUtc)
    {
        MarketId = MarketPriceTick.Required(marketId, nameof(marketId), 128);
        TokenId = MarketPriceTick.Required(tokenId, nameof(tokenId), 256);
        TimestampUtc = MarketPriceTick.RequireTimestamp(timestampUtc, nameof(timestampUtc));
        TimestampUnixMilliseconds = TimestampUtc.ToUnixTimeMilliseconds();
        BestBidPrice = bestBidPrice.HasValue ? MarketPriceTick.RequireNonNegative(bestBidPrice.Value, nameof(bestBidPrice)) : null;
        BestBidSize = bestBidSize.HasValue ? MarketPriceTick.RequireNonNegative(bestBidSize.Value, nameof(bestBidSize)) : null;
        BestAskPrice = bestAskPrice.HasValue ? MarketPriceTick.RequireNonNegative(bestAskPrice.Value, nameof(bestAskPrice)) : null;
        BestAskSize = bestAskSize.HasValue ? MarketPriceTick.RequireNonNegative(bestAskSize.Value, nameof(bestAskSize)) : null;
        Spread = BestBidPrice.HasValue && BestAskPrice.HasValue ? BestAskPrice.Value - BestBidPrice.Value : null;
        SourceName = MarketPriceTick.Required(sourceName, nameof(sourceName), 128);
        SourceSequence = MarketPriceTick.Optional(sourceSequence, 256);
        RawJson = string.IsNullOrWhiteSpace(rawJson) ? "{}" : rawJson.Trim();
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc.ToUniversalTime();
    }

    public string MarketId { get; private set; }

    public string TokenId { get; private set; }

    public DateTimeOffset TimestampUtc { get; private set; }

    public long TimestampUnixMilliseconds { get; private set; }

    public decimal? BestBidPrice { get; private set; }

    public decimal? BestBidSize { get; private set; }

    public decimal? BestAskPrice { get; private set; }

    public decimal? BestAskSize { get; private set; }

    public decimal? Spread { get; private set; }

    public string SourceName { get; private set; }

    public string? SourceSequence { get; private set; }

    public string RawJson { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
