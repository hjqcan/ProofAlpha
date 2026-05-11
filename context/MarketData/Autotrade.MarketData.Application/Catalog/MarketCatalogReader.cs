using Autotrade.MarketData.Application.Contract.Catalog;
using Microsoft.Extensions.Logging;

namespace Autotrade.MarketData.Application.Catalog;

/// <summary>
/// Adapter to expose MarketCatalog as a read-only contract.
/// </summary>
public sealed class MarketCatalogReader : IMarketCatalogReader
{
    private readonly IMarketCatalog _catalog;
    private readonly ILogger<MarketCatalogReader> _logger;

    public MarketCatalogReader(IMarketCatalog catalog, ILogger<MarketCatalogReader> logger)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public MarketInfoDto? GetMarket(string marketId)
    {
        if (string.IsNullOrWhiteSpace(marketId))
        {
            return null;
        }

        var market = _catalog.GetMarket(marketId);
        return market is null ? null : Map(market);
    }

    public IReadOnlyList<MarketInfoDto> GetAllMarkets()
        => _catalog.GetAllMarkets().Select(Map).ToList();

    public IReadOnlyList<MarketInfoDto> GetActiveMarkets()
        => _catalog.GetActiveMarkets().Select(Map).ToList();

    public IReadOnlyList<MarketInfoDto> GetLiquidMarkets(decimal minVolume)
        => _catalog.GetLiquidMarkets(minVolume).Select(Map).ToList();

    public IReadOnlyList<MarketInfoDto> GetExpiringMarkets(TimeSpan within)
        => _catalog.GetExpiringMarkets(within).Select(Map).ToList();

    private static MarketInfoDto Map(MarketInfo market)
        => new()
        {
            MarketId = market.MarketId,
            ConditionId = market.ConditionId,
            Name = market.Name,
            Category = market.Category,
            Slug = market.Slug,
            Status = market.Status.ToString(),
            ExpiresAtUtc = market.ExpiresAtUtc,
            Volume24h = market.Volume24h,
            Liquidity = market.Liquidity,
            TokenIds = market.TokenIds
        };
}
