using Autotrade.Application.Services;

namespace Autotrade.MarketData.Application.Contract.Spot;

public sealed record SpotPriceTick(
    string Symbol,
    decimal Price,
    DateTimeOffset TimestampUtc,
    string Source);

public sealed record SpotBar(
    string Symbol,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    string Source,
    int TickCount);

public sealed record SpotPriceUpdateResult(
    bool Accepted,
    bool UpdatedLatest,
    SpotPriceTick? Tick,
    string? RejectedReason = null);

public interface ISpotPriceStore : IApplicationService
{
    SpotPriceUpdateResult UpdateTick(SpotPriceTick tick);

    bool TryGetLatest(string symbol, out SpotPriceTick tick);

    bool TryGetLatest(string symbol, TimeSpan maxAge, DateTimeOffset nowUtc, out SpotPriceTick tick);

    bool TryGetAtOrBefore(
        string symbol,
        DateTimeOffset timestampUtc,
        TimeSpan maxLookback,
        out SpotPriceTick tick);

    IReadOnlyList<SpotPriceTick> GetRecentTicks(string symbol, int maxCount);

    IReadOnlyList<SpotBar> GetRecentBars(string symbol, TimeSpan interval, DateTimeOffset sinceUtc);
}

public interface ISpotPriceFeed : IApplicationService
{
    bool IsRunning { get; }

    Task StartAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
