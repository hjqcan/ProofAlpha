using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.OrderBook;
using Autotrade.MarketData.Application.Contract.Snapshots;
using Autotrade.MarketData.Application.Contract.Spot;
using Autotrade.MarketData.Application.Contract.Windows;

namespace Autotrade.MarketData.Application.Snapshots;

public sealed class MarketDataSnapshotReader : IMarketDataSnapshotReader
{
    private readonly IMarketCatalogReader _catalogReader;
    private readonly IOrderBookReader _orderBookReader;
    private readonly ISpotPriceStore _spotPriceStore;
    private readonly IMarketWindowSpecProvider _windowSpecProvider;

    public MarketDataSnapshotReader(
        IMarketCatalogReader catalogReader,
        IOrderBookReader orderBookReader,
        ISpotPriceStore spotPriceStore,
        IMarketWindowSpecProvider windowSpecProvider)
    {
        _catalogReader = catalogReader ?? throw new ArgumentNullException(nameof(catalogReader));
        _orderBookReader = orderBookReader ?? throw new ArgumentNullException(nameof(orderBookReader));
        _spotPriceStore = spotPriceStore ?? throw new ArgumentNullException(nameof(spotPriceStore));
        _windowSpecProvider = windowSpecProvider ?? throw new ArgumentNullException(nameof(windowSpecProvider));
    }

    public UnifiedMarketDataSnapshot? GetSnapshot(
        string marketId,
        TimeSpan maxSpotAge,
        TimeSpan maxOrderBookAge,
        int depthLevels = 10,
        TimeSpan? maxBaselineSpotAge = null)
    {
        if (string.IsNullOrWhiteSpace(marketId))
        {
            return null;
        }

        var market = _catalogReader.GetMarket(marketId);
        if (market is null)
        {
            return null;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var spec = _windowSpecProvider.TryCreate(market);
        var baselineMaxAge = maxBaselineSpotAge ?? maxSpotAge;

        SpotPriceTick? latestSpot = null;
        SpotPriceTick? baselineSpot = null;
        var spotFresh = false;
        var spotReason = "window_spec_missing";
        TimeSpan? spotAge = null;
        var baselineSpotFresh = false;
        var baselineSpotReason = "window_spec_missing";
        TimeSpan? baselineSpotAge = null;

        if (spec is not null)
        {
            if (_spotPriceStore.TryGetLatest(spec.UnderlyingSymbol, out var latest))
            {
                latestSpot = latest;
                spotAge = AbsoluteAge(nowUtc, latest.TimestampUtc);
                spotFresh = spotAge <= maxSpotAge;
                spotReason = spotFresh ? null : "spot_stale";
            }
            else
            {
                spotReason = "spot_missing";
            }

            if (_spotPriceStore.TryGetAtOrBefore(
                    spec.UnderlyingSymbol,
                    spec.WindowStartUtc,
                    baselineMaxAge,
                    out var baseline))
            {
                baselineSpot = baseline;
                baselineSpotAge = AbsoluteAge(spec.WindowStartUtc, baseline.TimestampUtc);
                baselineSpotFresh = baselineSpotAge <= baselineMaxAge;
                baselineSpotReason = baselineSpotFresh ? null : "baseline_spot_stale";
            }
            else
            {
                baselineSpotReason = "baseline_spot_missing_or_stale";
            }
        }

        var yesTop = market.TokenIds.Count > 0 ? _orderBookReader.GetTopOfBook(market.TokenIds[0]) : null;
        var noTop = market.TokenIds.Count > 1 ? _orderBookReader.GetTopOfBook(market.TokenIds[1]) : null;
        var yesDepth = market.TokenIds.Count > 0
            ? _orderBookReader.GetDepth(market.TokenIds[0], depthLevels)
            : Array.Empty<PriceLevelDto>();
        var noDepth = market.TokenIds.Count > 1
            ? _orderBookReader.GetDepth(market.TokenIds[1], depthLevels)
            : Array.Empty<PriceLevelDto>();

        var orderBookAge = GetOrderBookAge(nowUtc, yesTop, noTop);
        var orderBookFresh = orderBookAge is not null && orderBookAge <= maxOrderBookAge;

        return new UnifiedMarketDataSnapshot(
            market,
            spec,
            latestSpot,
            baselineSpot,
            yesTop,
            noTop,
            yesDepth,
            noDepth,
            new MarketDataStaleness(spotFresh, spotAge, spotReason),
            new MarketDataStaleness(baselineSpotFresh, baselineSpotAge, baselineSpotReason),
            new MarketDataStaleness(
                orderBookFresh,
                orderBookAge,
                orderBookFresh ? null : orderBookAge is null ? "orderbook_missing" : "orderbook_stale"),
            nowUtc);
    }

    public MarketWindowSpec? GetWindowSpec(string marketId)
        => _windowSpecProvider.GetSpec(marketId);

    private static TimeSpan? GetOrderBookAge(DateTimeOffset nowUtc, TopOfBookDto? yesTop, TopOfBookDto? noTop)
    {
        var timestamps = new[] { yesTop?.LastUpdatedUtc, noTop?.LastUpdatedUtc }
            .Where(timestamp => timestamp.HasValue)
            .Select(timestamp => timestamp!.Value)
            .ToArray();

        if (timestamps.Length == 0)
        {
            return null;
        }

        return timestamps.Max(timestamp => AbsoluteAge(nowUtc, timestamp));
    }

    private static TimeSpan AbsoluteAge(DateTimeOffset nowUtc, DateTimeOffset timestampUtc)
    {
        var timestamp = timestampUtc.ToUniversalTime();
        return nowUtc >= timestamp ? nowUtc - timestamp : timestamp - nowUtc;
    }
}
