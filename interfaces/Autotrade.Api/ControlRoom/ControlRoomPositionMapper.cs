using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.OrderBook;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Api.ControlRoom;

internal static class ControlRoomPositionMapper
{
    public static ControlRoomPositionDto Map(
        PositionDto position,
        IMarketCatalogReader? catalogReader,
        IOrderBookReader? orderBookReader,
        IReadOnlyDictionary<string, PositionExitMark>? fallbackMarks = null,
        IReadOnlyDictionary<string, string>? tokenOverrides = null)
    {
        var tokenId = ResolveTokenId(position, catalogReader?.GetMarket(position.MarketId), tokenOverrides);
        return Map(position, tokenId, orderBookReader, fallbackMarks);
    }

    public static ControlRoomPositionDto Map(
        PositionDto position,
        ControlRoomMarketDto market,
        IOrderBookReader? orderBookReader,
        IReadOnlyDictionary<string, PositionExitMark>? fallbackMarks = null)
    {
        var tokenId = ResolveTokenId(position.Outcome, market.Tokens);
        return Map(position, tokenId, orderBookReader, fallbackMarks);
    }

    public static string? ResolveTokenId(
        PositionDto position,
        MarketInfoDto? market,
        IReadOnlyDictionary<string, string>? tokenOverrides = null)
    {
        var catalogTokenId = market is null ? null : ResolveTokenId(position.Outcome, market.TokenIds);
        if (!string.IsNullOrWhiteSpace(catalogTokenId))
        {
            return catalogTokenId;
        }

        return tokenOverrides?.TryGetValue(BuildPositionKey(position.MarketId, position.Outcome), out var tokenId) == true
            ? tokenId
            : null;
    }

    public static string BuildPositionKey(string marketId, OutcomeSide outcome)
        => $"{marketId}|{outcome}";

    private static ControlRoomPositionDto Map(
        PositionDto position,
        string? tokenId,
        IOrderBookReader? orderBookReader,
        IReadOnlyDictionary<string, PositionExitMark>? fallbackMarks)
    {
        var fallbackMark = !string.IsNullOrWhiteSpace(tokenId) &&
            fallbackMarks?.TryGetValue(tokenId, out var resolvedFallback) == true
                ? resolvedFallback
                : null;
        var mark = ResolveExitMark(tokenId, orderBookReader, fallbackMark);
        var unrealizedPnl = mark.Price.HasValue
            ? position.UnrealizedPnl(mark.Price.Value)
            : (decimal?)null;
        var totalPnl = unrealizedPnl.HasValue
            ? position.RealizedPnl + unrealizedPnl.Value
            : (decimal?)null;
        var returnPct = totalPnl.HasValue && position.Notional > 0m
            ? totalPnl.Value / position.Notional * 100m
            : (decimal?)null;

        return new ControlRoomPositionDto(
            position.MarketId,
            position.Outcome.ToString(),
            position.Quantity,
            position.AverageCost,
            position.Notional,
            position.RealizedPnl,
            mark.Price,
            unrealizedPnl,
            totalPnl,
            returnPct,
            mark.Source,
            position.UpdatedAtUtc);
    }

    private static (decimal? Price, string Source) ResolveExitMark(
        string? tokenId,
        IOrderBookReader? orderBookReader,
        PositionExitMark? fallbackMark)
    {
        if (string.IsNullOrWhiteSpace(tokenId))
        {
            return (null, "Unavailable");
        }

        var top = orderBookReader?.GetTopOfBook(tokenId);
        if (top?.BestBidPrice is not null)
        {
            return (top.BestBidPrice.Value, "LocalBestBid");
        }

        return fallbackMark is null
            ? (null, "Unavailable")
            : (fallbackMark.Price, fallbackMark.Source);
    }

    private static string? ResolveTokenId(OutcomeSide outcome, IReadOnlyList<string> tokenIds)
    {
        if (tokenIds.Count == 0)
        {
            return null;
        }

        var tokenIndex = outcome == OutcomeSide.Yes ? 0 : 1;
        return tokenIds.Count > tokenIndex ? tokenIds[tokenIndex] : null;
    }

    private static string? ResolveTokenId(OutcomeSide outcome, IReadOnlyList<ControlRoomMarketTokenDto> tokens)
    {
        if (tokens.Count == 0)
        {
            return null;
        }

        var byOutcome = tokens.FirstOrDefault(token =>
            string.Equals(token.Outcome, outcome.ToString(), StringComparison.OrdinalIgnoreCase));
        if (byOutcome is not null)
        {
            return byOutcome.TokenId;
        }

        var tokenIndex = outcome == OutcomeSide.Yes ? 0 : 1;
        return tokens.Count > tokenIndex ? tokens[tokenIndex].TokenId : null;
    }
}

public sealed record PositionExitMark(decimal? Price, string Source);
