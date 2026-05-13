using Autotrade.Application.Services;

namespace Autotrade.MarketData.Application.Contract.Tape;

public sealed record MarketPriceTickDto(
    Guid Id,
    string MarketId,
    string TokenId,
    DateTimeOffset TimestampUtc,
    decimal Price,
    decimal? Size,
    string SourceName,
    string? SourceSequence,
    string RawJson,
    DateTimeOffset CreatedAtUtc);

public sealed record OrderBookTopTickDto(
    Guid Id,
    string MarketId,
    string TokenId,
    DateTimeOffset TimestampUtc,
    decimal? BestBidPrice,
    decimal? BestBidSize,
    decimal? BestAskPrice,
    decimal? BestAskSize,
    decimal? Spread,
    string SourceName,
    string? SourceSequence,
    string RawJson,
    DateTimeOffset CreatedAtUtc);

public sealed record OrderBookDepthLevelDto(
    decimal Price,
    decimal Size,
    bool IsBid);

public sealed record OrderBookDepthSnapshotDto(
    Guid Id,
    string MarketId,
    string TokenId,
    DateTimeOffset TimestampUtc,
    string SnapshotHash,
    IReadOnlyList<OrderBookDepthLevelDto> Bids,
    IReadOnlyList<OrderBookDepthLevelDto> Asks,
    string SourceName,
    string RawJson,
    DateTimeOffset CreatedAtUtc);

public sealed record ClobTradeTickDto(
    Guid Id,
    string MarketId,
    string TokenId,
    string ExchangeTradeId,
    DateTimeOffset TimestampUtc,
    decimal Price,
    decimal Size,
    string Side,
    decimal? FeeRateBps,
    string SourceName,
    string RawJson,
    DateTimeOffset CreatedAtUtc);

public sealed record MarketResolutionEventDto(
    Guid Id,
    string MarketId,
    DateTimeOffset ResolvedAtUtc,
    string Outcome,
    string SourceName,
    string RawJson,
    DateTimeOffset CreatedAtUtc);

public sealed record MarketTapeQuery(
    string MarketId,
    string? TokenId = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    DateTimeOffset? AsOfUtc = null,
    int Limit = 5000);

public sealed record MarketTapeReplaySlice(
    MarketTapeQuery Query,
    IReadOnlyList<MarketPriceTickDto> PriceTicks,
    IReadOnlyList<OrderBookTopTickDto> TopTicks,
    IReadOnlyList<OrderBookDepthSnapshotDto> DepthSnapshots,
    IReadOnlyList<ClobTradeTickDto> TradeTicks,
    IReadOnlyList<MarketResolutionEventDto> ResolutionEvents,
    IReadOnlyList<string> CompletenessNotes);

public interface IMarketTapeWriter : IApplicationService
{
    Task AppendMarketPriceTicksAsync(
        IReadOnlyList<MarketPriceTickDto> ticks,
        CancellationToken cancellationToken = default);

    Task AppendOrderBookTopTicksAsync(
        IReadOnlyList<OrderBookTopTickDto> ticks,
        CancellationToken cancellationToken = default);

    Task AppendOrderBookDepthSnapshotsAsync(
        IReadOnlyList<OrderBookDepthSnapshotDto> snapshots,
        CancellationToken cancellationToken = default);

    Task AppendClobTradeTicksAsync(
        IReadOnlyList<ClobTradeTickDto> ticks,
        CancellationToken cancellationToken = default);

    Task AppendMarketResolutionEventsAsync(
        IReadOnlyList<MarketResolutionEventDto> events,
        CancellationToken cancellationToken = default);
}

public interface IMarketTapeReader : IApplicationService
{
    Task<IReadOnlyList<OrderBookTopTickDto>> GetTopTicksAsync(
        MarketTapeQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderBookDepthSnapshotDto>> GetDepthSnapshotsAsync(
        MarketTapeQuery query,
        CancellationToken cancellationToken = default);
}

public interface IMarketReplayReader : IApplicationService
{
    Task<MarketTapeReplaySlice> GetReplaySliceAsync(
        MarketTapeQuery query,
        CancellationToken cancellationToken = default);
}
