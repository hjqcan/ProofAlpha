using Autotrade.Application.Services;
using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.OrderBook;
using Autotrade.MarketData.Application.Contract.Spot;
using Autotrade.MarketData.Application.Contract.Windows;

namespace Autotrade.MarketData.Application.Contract.Snapshots;

public sealed record MarketDataStaleness(
    bool IsFresh,
    TimeSpan? Age,
    string? Reason);

public sealed record UnifiedMarketDataSnapshot(
    MarketInfoDto Market,
    MarketWindowSpec? WindowSpec,
    SpotPriceTick? LatestSpot,
    SpotPriceTick? BaselineSpot,
    TopOfBookDto? YesTopOfBook,
    TopOfBookDto? NoTopOfBook,
    IReadOnlyList<PriceLevelDto> YesDepth,
    IReadOnlyList<PriceLevelDto> NoDepth,
    MarketDataStaleness SpotStaleness,
    MarketDataStaleness BaselineSpotStaleness,
    MarketDataStaleness OrderBookStaleness,
    DateTimeOffset TimestampUtc);

public interface IMarketDataSnapshotReader : IApplicationService
{
    UnifiedMarketDataSnapshot? GetSnapshot(
        string marketId,
        TimeSpan maxSpotAge,
        TimeSpan maxOrderBookAge,
        int depthLevels = 10,
        TimeSpan? maxBaselineSpotAge = null);

    MarketWindowSpec? GetWindowSpec(string marketId);
}
