using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.OrderBook;

namespace Autotrade.Strategy.Application.Contract.Strategies;

/// <summary>
/// Market snapshot for strategy evaluation.
/// </summary>
public sealed record MarketSnapshot(
    MarketInfoDto Market,
    TopOfBookDto? YesTopOfBook,
    TopOfBookDto? NoTopOfBook,
    DateTimeOffset TimestampUtc)
{
    /// <summary>
    /// Convenience property for Market.MarketId.
    /// </summary>
    public string? MarketId => Market?.MarketId;
}
