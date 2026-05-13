using NetDevPack.Domain;

namespace Autotrade.MarketData.Domain.Entities;

public sealed class OrderBookDepthSnapshot : Entity, IAggregateRoot
{
    private OrderBookDepthSnapshot()
    {
        MarketId = string.Empty;
        TokenId = string.Empty;
        SnapshotHash = string.Empty;
        BidsJson = "[]";
        AsksJson = "[]";
        SourceName = string.Empty;
        RawJson = "{}";
        TimestampUtc = DateTimeOffset.UtcNow;
        TimestampUnixMilliseconds = TimestampUtc.ToUnixTimeMilliseconds();
        CreatedAtUtc = TimestampUtc;
    }

    public OrderBookDepthSnapshot(
        string marketId,
        string tokenId,
        DateTimeOffset timestampUtc,
        string snapshotHash,
        string bidsJson,
        string asksJson,
        string sourceName,
        string rawJson,
        DateTimeOffset createdAtUtc)
    {
        MarketId = MarketPriceTick.Required(marketId, nameof(marketId), 128);
        TokenId = MarketPriceTick.Required(tokenId, nameof(tokenId), 256);
        TimestampUtc = MarketPriceTick.RequireTimestamp(timestampUtc, nameof(timestampUtc));
        TimestampUnixMilliseconds = TimestampUtc.ToUnixTimeMilliseconds();
        SnapshotHash = MarketPriceTick.Required(snapshotHash, nameof(snapshotHash), 256);
        BidsJson = string.IsNullOrWhiteSpace(bidsJson) ? "[]" : bidsJson.Trim();
        AsksJson = string.IsNullOrWhiteSpace(asksJson) ? "[]" : asksJson.Trim();
        SourceName = MarketPriceTick.Required(sourceName, nameof(sourceName), 128);
        RawJson = string.IsNullOrWhiteSpace(rawJson) ? "{}" : rawJson.Trim();
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc.ToUniversalTime();
    }

    public string MarketId { get; private set; }

    public string TokenId { get; private set; }

    public DateTimeOffset TimestampUtc { get; private set; }

    public long TimestampUnixMilliseconds { get; private set; }

    public string SnapshotHash { get; private set; }

    public string BidsJson { get; private set; }

    public string AsksJson { get; private set; }

    public string SourceName { get; private set; }

    public string RawJson { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
