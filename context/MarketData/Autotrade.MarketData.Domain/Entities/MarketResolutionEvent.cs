using NetDevPack.Domain;

namespace Autotrade.MarketData.Domain.Entities;

public sealed class MarketResolutionEvent : Entity, IAggregateRoot
{
    private MarketResolutionEvent()
    {
        MarketId = string.Empty;
        Outcome = string.Empty;
        SourceName = string.Empty;
        RawJson = "{}";
        ResolvedAtUtc = DateTimeOffset.UtcNow;
        ResolvedUnixMilliseconds = ResolvedAtUtc.ToUnixTimeMilliseconds();
        CreatedAtUtc = ResolvedAtUtc;
    }

    public MarketResolutionEvent(
        string marketId,
        DateTimeOffset resolvedAtUtc,
        string outcome,
        string sourceName,
        string rawJson,
        DateTimeOffset createdAtUtc)
    {
        MarketId = MarketPriceTick.Required(marketId, nameof(marketId), 128);
        ResolvedAtUtc = MarketPriceTick.RequireTimestamp(resolvedAtUtc, nameof(resolvedAtUtc));
        ResolvedUnixMilliseconds = ResolvedAtUtc.ToUnixTimeMilliseconds();
        Outcome = MarketPriceTick.Required(outcome, nameof(outcome), 128);
        SourceName = MarketPriceTick.Required(sourceName, nameof(sourceName), 128);
        RawJson = string.IsNullOrWhiteSpace(rawJson) ? "{}" : rawJson.Trim();
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc.ToUniversalTime();
    }

    public string MarketId { get; private set; }

    public DateTimeOffset ResolvedAtUtc { get; private set; }

    public long ResolvedUnixMilliseconds { get; private set; }

    public string Outcome { get; private set; }

    public string SourceName { get; private set; }

    public string RawJson { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
