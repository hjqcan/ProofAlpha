using Autotrade.Application.Services;

namespace Autotrade.MarketData.Application.Contract.Catalog;

/// <summary>
/// Read-only market catalog interface.
/// </summary>
public interface IMarketCatalogReader : IApplicationService
{
    MarketInfoDto? GetMarket(string marketId);

    IReadOnlyList<MarketInfoDto> GetAllMarkets();

    IReadOnlyList<MarketInfoDto> GetActiveMarkets();

    IReadOnlyList<MarketInfoDto> GetLiquidMarkets(decimal minVolume);

    IReadOnlyList<MarketInfoDto> GetExpiringMarkets(TimeSpan within);
}
