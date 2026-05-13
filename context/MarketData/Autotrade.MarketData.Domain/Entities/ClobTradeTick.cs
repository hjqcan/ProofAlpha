using NetDevPack.Domain;

namespace Autotrade.MarketData.Domain.Entities;

public sealed class ClobTradeTick : Entity, IAggregateRoot
{
    private ClobTradeTick()
    {
        MarketId = string.Empty;
        TokenId = string.Empty;
        ExchangeTradeId = string.Empty;
        Side = string.Empty;
        SourceName = string.Empty;
        RawJson = "{}";
        TimestampUtc = DateTimeOffset.UtcNow;
        TimestampUnixMilliseconds = TimestampUtc.ToUnixTimeMilliseconds();
        CreatedAtUtc = TimestampUtc;
    }

    public ClobTradeTick(
        string marketId,
        string tokenId,
        string exchangeTradeId,
        DateTimeOffset timestampUtc,
        decimal price,
        decimal size,
        string side,
        decimal? feeRateBps,
        string sourceName,
        string rawJson,
        DateTimeOffset createdAtUtc)
    {
        MarketId = MarketPriceTick.Required(marketId, nameof(marketId), 128);
        TokenId = MarketPriceTick.Required(tokenId, nameof(tokenId), 256);
        ExchangeTradeId = MarketPriceTick.Required(exchangeTradeId, nameof(exchangeTradeId), 256);
        TimestampUtc = MarketPriceTick.RequireTimestamp(timestampUtc, nameof(timestampUtc));
        TimestampUnixMilliseconds = TimestampUtc.ToUnixTimeMilliseconds();
        Price = MarketPriceTick.RequireNonNegative(price, nameof(price));
        Size = MarketPriceTick.RequireNonNegative(size, nameof(size));
        Side = MarketPriceTick.Required(side, nameof(side), 16);
        FeeRateBps = feeRateBps.HasValue ? MarketPriceTick.RequireNonNegative(feeRateBps.Value, nameof(feeRateBps)) : null;
        SourceName = MarketPriceTick.Required(sourceName, nameof(sourceName), 128);
        RawJson = string.IsNullOrWhiteSpace(rawJson) ? "{}" : rawJson.Trim();
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc.ToUniversalTime();
    }

    public string MarketId { get; private set; }

    public string TokenId { get; private set; }

    public string ExchangeTradeId { get; private set; }

    public DateTimeOffset TimestampUtc { get; private set; }

    public long TimestampUnixMilliseconds { get; private set; }

    public decimal Price { get; private set; }

    public decimal Size { get; private set; }

    public string Side { get; private set; }

    public decimal? FeeRateBps { get; private set; }

    public string SourceName { get; private set; }

    public string RawJson { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
