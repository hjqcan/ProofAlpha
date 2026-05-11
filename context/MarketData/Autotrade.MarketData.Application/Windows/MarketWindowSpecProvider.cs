using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.Windows;
using Microsoft.Extensions.Options;

namespace Autotrade.MarketData.Application.Windows;

public sealed class MarketWindowSpecProvider : IMarketWindowSpecProvider
{
    private readonly IMarketCatalogReader _catalogReader;
    private readonly MarketWindowSpecOptions _options;

    public MarketWindowSpecProvider(
        IMarketCatalogReader catalogReader,
        IOptions<MarketWindowSpecOptions> options)
    {
        _catalogReader = catalogReader ?? throw new ArgumentNullException(nameof(catalogReader));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public MarketWindowSpec? GetSpec(string marketId)
    {
        if (string.IsNullOrWhiteSpace(marketId))
        {
            return null;
        }

        var market = _catalogReader.GetMarket(marketId);
        return market is null ? null : TryCreate(market);
    }

    public MarketWindowSpec? TryCreate(MarketInfoDto market)
    {
        ArgumentNullException.ThrowIfNull(market);

        return MarketWindowSpecParser.TryParseCryptoUpDown15m(
            market,
            _options.SettlementOracle.Trim(),
            _options.SettlementReference.Trim(),
            _options.OracleStatus);
    }
}
