using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.OrderBook;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Strategy.Application.Strategies.Common;

internal readonly record struct TopBookQuote(
    OutcomeSide Outcome,
    string TokenId,
    decimal BidPrice,
    decimal BidSize,
    decimal AskPrice,
    decimal AskSize,
    decimal Spread,
    DateTimeOffset LastUpdatedUtc);

internal static class StrategyTopBook
{
    public const decimal MinLimitPrice = 0.01m;
    public const decimal MaxLimitPrice = 0.99m;

    public static bool IsTradableBinaryMarket(MarketInfoDto market)
        => string.Equals(market.Status, "Active", StringComparison.OrdinalIgnoreCase)
           && market.TokenIds.Count >= 2;

    public static bool TryGetQuote(MarketSnapshot snapshot, OutcomeSide outcome, out TopBookQuote quote)
    {
        quote = default;

        if (snapshot.Market.TokenIds.Count < 2)
        {
            return false;
        }

        var top = outcome == OutcomeSide.Yes
            ? snapshot.YesTopOfBook
            : snapshot.NoTopOfBook;

        if (!TryReadTopOfBook(top, out var bid, out var bidSize, out var ask, out var askSize, out var spread))
        {
            return false;
        }

        if (bid <= 0m || ask <= 0m || ask < bid)
        {
            return false;
        }

        var tokenId = outcome == OutcomeSide.Yes
            ? snapshot.Market.TokenIds[0]
            : snapshot.Market.TokenIds[1];

        quote = new TopBookQuote(
            outcome,
            tokenId,
            bid,
            bidSize,
            ask,
            askSize,
            spread,
            top!.LastUpdatedUtc);

        return true;
    }

    public static IReadOnlyList<TopBookQuote> GetQuotes(MarketSnapshot snapshot)
    {
        var quotes = new List<TopBookQuote>(2);

        if (TryGetQuote(snapshot, OutcomeSide.Yes, out var yesQuote))
        {
            quotes.Add(yesQuote);
        }

        if (TryGetQuote(snapshot, OutcomeSide.No, out var noQuote))
        {
            quotes.Add(noQuote);
        }

        return quotes;
    }

    public static bool IsFresh(MarketSnapshot snapshot, TopBookQuote quote, int maxAgeSeconds)
    {
        if (maxAgeSeconds <= 0)
        {
            return true;
        }

        var maxAge = TimeSpan.FromSeconds(maxAgeSeconds);
        var age = snapshot.TimestampUtc >= quote.LastUpdatedUtc
            ? snapshot.TimestampUtc - quote.LastUpdatedUtc
            : quote.LastUpdatedUtc - snapshot.TimestampUtc;

        return age <= maxAge;
    }

    public static bool IsNegRisk(MarketSnapshot snapshot)
        => snapshot.Market.Slug?.Contains("neg", StringComparison.OrdinalIgnoreCase) == true;

    public static decimal ClampPrice(decimal price)
        => Math.Clamp(price, MinLimitPrice, MaxLimitPrice);

    public static decimal CalculateQuantity(
        decimal defaultQuantity,
        decimal minQuantity,
        decimal maxNotionalPerOrder,
        decimal remainingMarketNotional,
        decimal price,
        decimal availableSize)
    {
        if (price <= 0m || availableSize <= 0m || remainingMarketNotional <= 0m)
        {
            return 0m;
        }

        var maxByOrder = maxNotionalPerOrder / Math.Max(price, MinLimitPrice);
        var maxByMarket = remainingMarketNotional / Math.Max(price, MinLimitPrice);
        var quantity = Math.Min(defaultQuantity, Math.Min(availableSize, Math.Min(maxByOrder, maxByMarket)));

        return quantity >= minQuantity ? quantity : 0m;
    }

    private static bool TryReadTopOfBook(
        TopOfBookDto? top,
        out decimal bid,
        out decimal bidSize,
        out decimal ask,
        out decimal askSize,
        out decimal spread)
    {
        bid = 0m;
        bidSize = 0m;
        ask = 0m;
        askSize = 0m;
        spread = 0m;

        if (top?.BestBidPrice is null ||
            top.BestBidSize is null ||
            top.BestAskPrice is null ||
            top.BestAskSize is null)
        {
            return false;
        }

        bid = top.BestBidPrice.Value;
        bidSize = top.BestBidSize.Value;
        ask = top.BestAskPrice.Value;
        askSize = top.BestAskSize.Value;
        spread = top.Spread ?? ask - bid;

        return bidSize > 0m && askSize > 0m && spread >= 0m;
    }
}
