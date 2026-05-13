using NetDevPack.Domain;

namespace Autotrade.MarketData.Domain.Entities;

public sealed class MarketPriceTick : Entity, IAggregateRoot
{
    private MarketPriceTick()
    {
        MarketId = string.Empty;
        TokenId = string.Empty;
        SourceName = string.Empty;
        RawJson = "{}";
        TimestampUtc = DateTimeOffset.UtcNow;
        TimestampUnixMilliseconds = TimestampUtc.ToUnixTimeMilliseconds();
        CreatedAtUtc = TimestampUtc;
    }

    public MarketPriceTick(
        string marketId,
        string tokenId,
        DateTimeOffset timestampUtc,
        decimal price,
        decimal? size,
        string sourceName,
        string? sourceSequence,
        string rawJson,
        DateTimeOffset createdAtUtc)
    {
        MarketId = Required(marketId, nameof(marketId), 128);
        TokenId = Required(tokenId, nameof(tokenId), 256);
        TimestampUtc = RequireTimestamp(timestampUtc, nameof(timestampUtc));
        TimestampUnixMilliseconds = TimestampUtc.ToUnixTimeMilliseconds();
        Price = RequireNonNegative(price, nameof(price));
        Size = size.HasValue ? RequireNonNegative(size.Value, nameof(size)) : null;
        SourceName = Required(sourceName, nameof(sourceName), 128);
        SourceSequence = Optional(sourceSequence, 256);
        RawJson = string.IsNullOrWhiteSpace(rawJson) ? "{}" : rawJson.Trim();
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc.ToUniversalTime();
    }

    public string MarketId { get; private set; }

    public string TokenId { get; private set; }

    public DateTimeOffset TimestampUtc { get; private set; }

    public long TimestampUnixMilliseconds { get; private set; }

    public decimal Price { get; private set; }

    public decimal? Size { get; private set; }

    public string SourceName { get; private set; }

    public string? SourceSequence { get; private set; }

    public string RawJson { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    internal static string Required(string value, string paramName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{paramName} cannot be empty.", paramName);
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    internal static string? Optional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    internal static DateTimeOffset RequireTimestamp(DateTimeOffset value, string paramName)
    {
        if (value == default)
        {
            throw new ArgumentException($"{paramName} cannot be default.", paramName);
        }

        return value.ToUniversalTime();
    }

    internal static decimal RequireNonNegative(decimal value, string paramName)
    {
        if (value < 0m)
        {
            throw new ArgumentOutOfRangeException(paramName, value, $"{paramName} cannot be negative.");
        }

        return value;
    }
}
