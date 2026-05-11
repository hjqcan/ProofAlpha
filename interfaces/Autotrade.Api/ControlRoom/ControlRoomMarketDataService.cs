using System.Globalization;
using System.Text.Json;
using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.OrderBook;
using Autotrade.Polymarket.Abstractions;
using Autotrade.Polymarket.Models;
using Autotrade.Strategy.Application.Decisions;
using Autotrade.Trading.Application.Contract.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Autotrade.Api.ControlRoom;

public sealed class ControlRoomMarketDataService(
    IServiceProvider serviceProvider,
    IMemoryCache cache,
    IOptionsMonitor<ControlRoomOptions> controlRoomOptions,
    ILogger<ControlRoomMarketDataService> logger) : IControlRoomMarketDataService
{
    private const string MarketCacheKey = "control-room:markets:v5";

    public async Task<ControlRoomMarketsResponse> GetMarketsAsync(
        ControlRoomMarketDiscoveryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var now = DateTimeOffset.UtcNow;
        var effectiveLimit = Math.Clamp(query.Limit ?? controlRoomOptions.CurrentValue.MarketLimit, 1, 250);
        var effectiveOffset = Math.Max(0, query.Offset ?? 0);
        var requiredFilteredCount = effectiveOffset + effectiveLimit;
        var universe = await LoadMarketUniverseAsync(
            markets =>
            {
                var enrichedMarkets = ControlRoomMarketDiscoveryRanker.EnrichMarkets(markets, now);
                return ControlRoomMarketDiscoveryRanker.FilterMarkets(enrichedMarkets, query, now).Count >= requiredFilteredCount;
            },
            cancellationToken).ConfigureAwait(false);
        var allMarkets = ControlRoomMarketDiscoveryRanker.EnrichMarkets(universe.Markets, now);
        var filtered = ControlRoomMarketDiscoveryRanker.FilterMarkets(allMarkets, query, now);
        var sorted = ControlRoomMarketDiscoveryRanker.SortMarkets(filtered, query.Sort);
        var markets = sorted.Skip(effectiveOffset).Take(effectiveLimit).ToArray();
        var categories = allMarkets
            .Select(market => market.Category)
            .Where(categoryName => !string.IsNullOrWhiteSpace(categoryName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(categoryName => categoryName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ControlRoomMarketsResponse(
            DateTimeOffset.UtcNow,
            ResolveSource(markets.Length > 0 ? markets : allMarkets),
            sorted.Count,
            universe.IsComplete,
            categories,
            markets);
    }

    public async Task<ControlRoomMarketDetailResponse?> GetMarketDetailAsync(
        string marketId,
        int? levels,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marketId);

        var universe = await LoadMarketUniverseAsync(
            markets => markets.Any(item => IsMarketMatch(item, marketId)),
            cancellationToken).ConfigureAwait(false);
        var markets = universe.Markets;
        var rawMarket = markets.FirstOrDefault(item => IsMarketMatch(item, marketId));
        if (rawMarket is null)
        {
            return null;
        }

        var market = ControlRoomMarketDiscoveryRanker.EnrichMarket(rawMarket, DateTimeOffset.UtcNow);
        var orderBook = await GetOrderBookAsync(
            market.MarketId,
            tokenId: null,
            outcome: "Yes",
            levels,
            cancellationToken).ConfigureAwait(false);

        var orders = await GetRelatedOrdersAsync(market.MarketId, cancellationToken).ConfigureAwait(false);
        var positions = await GetRelatedPositionsAsync(market, cancellationToken).ConfigureAwait(false);
        var decisions = await GetRelatedDecisionsAsync(market.MarketId, cancellationToken).ConfigureAwait(false);

        return new ControlRoomMarketDetailResponse(
            DateTimeOffset.UtcNow,
            market.Source,
            market,
            orderBook,
            orders,
            positions,
            decisions,
            orderBook is null ? Array.Empty<ControlRoomMetricDto>() : BuildMicrostructureMetrics(orderBook, market));
    }

    public async Task<ControlRoomOrderBookDto?> GetOrderBookAsync(
        string marketId,
        string? tokenId,
        string? outcome,
        int? levels,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marketId);

        var effectiveLevels = Math.Clamp(levels ?? controlRoomOptions.CurrentValue.OrderBookLevels, 1, 50);
        var universe = await LoadMarketUniverseAsync(
            markets => markets.Any(item => IsMarketMatch(item, marketId)),
            cancellationToken).ConfigureAwait(false);
        var markets = universe.Markets;
        var market = markets.FirstOrDefault(item => IsMarketMatch(item, marketId));
        if (market is null)
        {
            return null;
        }

        var selected = ResolveToken(market, tokenId, outcome);
        if (selected is null)
        {
            return null;
        }

        var cacheKey = $"control-room:order-book:{selected.TokenId}:{effectiveLevels}";
        var cached = await cache.GetOrCreateAsync(
            cacheKey,
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(
                    Math.Clamp(controlRoomOptions.CurrentValue.OrderBookCacheTtlSeconds, 1, 60));

                return await LoadOrderBookFreshAsync(
                    market,
                    selected,
                    effectiveLevels,
                    cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);

        return cached;
    }

    private async Task<MarketUniverseSnapshot> LoadMarketUniverseAsync(
        Func<IReadOnlyList<ControlRoomMarketDto>, bool> hasEnough,
        CancellationToken cancellationToken)
    {
        var state = cache.GetOrCreate(
            MarketCacheKey,
            entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(
                    Math.Clamp(controlRoomOptions.CurrentValue.MarketCacheTtlSeconds, 5, 600));

                return new MarketUniverseState();
            }) ?? new MarketUniverseState();

        await state.LoadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (!state.IsComplete && !hasEnough(state.Markets))
            {
                var advanced = await LoadNextMarketPageAsync(state, cancellationToken).ConfigureAwait(false);
                if (!advanced)
                {
                    break;
                }
            }

            return new MarketUniverseSnapshot(state.Markets.ToArray(), state.IsComplete);
        }
        finally
        {
            state.LoadGate.Release();
        }
    }

    private async Task<bool> LoadNextMarketPageAsync(
        MarketUniverseState state,
        CancellationToken cancellationToken)
    {
        if (!controlRoomOptions.CurrentValue.EnablePublicMarketData)
        {
            return LoadCatalogMarkets(state);
        }

        var gammaClient = serviceProvider.GetService<IPolymarketGammaClient>();
        if (gammaClient is null)
        {
            return LoadCatalogMarkets(state);
        }

        var pageSize = Math.Clamp(controlRoomOptions.CurrentValue.MarketDiscoveryPageSize, 50, 500);
        var result = await gammaClient
            .ListMarketsAsync(pageSize, state.NextOffset, closed: false, order: "volume24hr", ascending: false, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess || result.Data is null)
        {
            logger.LogInformation(
                "Gamma market page unavailable for control room. Offset={Offset}, Limit={Limit}, StatusCode={StatusCode}, Error={Error}",
                state.NextOffset,
                pageSize,
                result.StatusCode,
                result.Error?.Message);

            return state.Markets.Count == 0 && LoadCatalogMarkets(state);
        }

        if (result.Data.Count == 0)
        {
            state.IsComplete = true;
            return true;
        }

        var beforeCount = state.Markets.Count;
        for (var pageIndex = 0; pageIndex < result.Data.Count; pageIndex++)
        {
            var gammaMarket = result.Data[pageIndex];
            var mapped = MapGammaMarket(gammaMarket, state.NextOffset + pageIndex);
            if (gammaMarket.Closed == true || mapped.Tokens.Count == 0)
            {
                continue;
            }

            if (state.SeenMarketIds.Add(mapped.MarketId))
            {
                state.Markets.Add(mapped);
            }
        }

        state.NextOffset += result.Data.Count;
        if (result.Data.Count < pageSize)
        {
            state.IsComplete = true;
        }

        logger.LogInformation(
            "Loaded Gamma market page for control room discovery. Added={AddedCount}, Cached={CachedCount}, NextOffset={NextOffset}, Complete={Complete}",
            state.Markets.Count - beforeCount,
            state.Markets.Count,
            state.NextOffset,
            state.IsComplete);

        return result.Data.Count > 0;
    }

    private bool LoadCatalogMarkets(MarketUniverseState state)
    {
        if (state.Markets.Count > 0 || state.IsComplete)
        {
            state.IsComplete = true;
            return false;
        }

        var catalog = serviceProvider.GetService<IMarketCatalogReader>();
        if (catalog is null)
        {
            state.IsComplete = true;
            return false;
        }

        var active = catalog.GetActiveMarkets()
            .OrderByDescending(item => item.Volume24h)
            .Select((item, index) => MapCatalogMarket(item, index))
            .ToArray();

        foreach (var market in active)
        {
            if (state.SeenMarketIds.Add(market.MarketId))
            {
                state.Markets.Add(market);
            }
        }

        state.IsComplete = true;
        return state.Markets.Count > 0;
    }

    private async Task<ControlRoomOrderBookDto?> LoadOrderBookFreshAsync(
        ControlRoomMarketDto market,
        ControlRoomMarketTokenDto token,
        int levels,
        CancellationToken cancellationToken)
    {
        var localReader = serviceProvider.GetService<IOrderBookReader>();
        var localDepth = localReader?.GetDepth(token.TokenId, levels) ?? Array.Empty<PriceLevelDto>();
        if (localDepth.Count > 0)
        {
            var observedAtUtc = DateTimeOffset.UtcNow;
            var lastUpdatedUtc = localReader?.GetTopOfBook(token.TokenId)?.LastUpdatedUtc ?? observedAtUtc;

            return BuildOrderBook(
                market,
                token,
                localDepth.Where(level => level.IsBid).Select(level => (level.Price, level.Size)),
                localDepth.Where(level => !level.IsBid).Select(level => (level.Price, level.Size)),
                lastUpdatedUtc,
                observedAtUtc,
                "LocalOrderBook");
        }

        var clobClient = serviceProvider.GetService<IPolymarketClobClient>();
        if (controlRoomOptions.CurrentValue.EnablePublicMarketData &&
            clobClient is not null &&
            LooksLikePolymarketToken(token.TokenId))
        {
            var result = await clobClient.GetOrderBookAsync(token.TokenId, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess && result.Data is not null)
            {
                return BuildOrderBook(
                    market,
                    token,
                    result.Data.Bids.Select(level => (ParseDecimal(level.Price), ParseDecimal(level.Size))).Take(levels),
                    result.Data.Asks.Select(level => (ParseDecimal(level.Price), ParseDecimal(level.Size))).Take(levels),
                    ParseTimestamp(result.Data.Timestamp),
                    DateTimeOffset.UtcNow,
                    "LiveClob");
            }

            logger.LogInformation(
                "CLOB order book unavailable for control room. TokenId={TokenId}, StatusCode={StatusCode}, Error={Error}",
                token.TokenId,
                result.StatusCode,
                result.Error?.Message);
        }

        return null;
    }

    private async Task<IReadOnlyList<ControlRoomOrderDto>> GetRelatedOrdersAsync(
        string marketId,
        CancellationToken cancellationToken)
    {
        var repository = serviceProvider.GetService<IOrderRepository>();
        if (repository is not null)
        {
            var orders = await repository
                .GetByMarketIdAsync(marketId, DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow, 20, cancellationToken)
                .ConfigureAwait(false);

            return orders
                .OrderByDescending(order => order.UpdatedAtUtc)
                .Select(order => new ControlRoomOrderDto(
                    order.ClientOrderId ?? order.Id.ToString("N"),
                    order.StrategyId ?? "manual",
                    order.MarketId,
                    order.Side.ToString(),
                    order.Outcome.ToString(),
                    order.Price,
                    order.Quantity,
                    order.FilledQuantity,
                    order.Status.ToString(),
                    order.UpdatedAtUtc))
                .ToArray();
        }

        return Array.Empty<ControlRoomOrderDto>();
    }

    private async Task<IReadOnlyList<ControlRoomPositionDto>> GetRelatedPositionsAsync(
        ControlRoomMarketDto market,
        CancellationToken cancellationToken)
    {
        var repository = serviceProvider.GetService<IPositionRepository>();
        if (repository is not null)
        {
            var orderBookReader = serviceProvider.GetService<IOrderBookReader>();
            var positions = await repository.GetNonZeroAsync(cancellationToken).ConfigureAwait(false);
            return positions
                .Where(position => string.Equals(position.MarketId, market.MarketId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(position => position.Notional)
                .Select(position => ControlRoomPositionMapper.Map(position, market, orderBookReader))
                .ToArray();
        }

        return Array.Empty<ControlRoomPositionDto>();
    }

    private async Task<IReadOnlyList<ControlRoomDecisionDto>> GetRelatedDecisionsAsync(
        string marketId,
        CancellationToken cancellationToken)
    {
        var queryService = serviceProvider.GetService<IStrategyDecisionQueryService>();
        if (queryService is not null)
        {
            var now = DateTimeOffset.UtcNow;
            var decisions = await queryService
                .QueryAsync(new StrategyDecisionQuery(null, marketId, now.AddDays(-7), now, 20), cancellationToken)
                .ConfigureAwait(false);

            return decisions
                .OrderByDescending(decision => decision.TimestampUtc)
                .Select(decision => new ControlRoomDecisionDto(
                    decision.StrategyId,
                    decision.Action,
                    decision.MarketId ?? marketId,
                    decision.Reason,
                    decision.TimestampUtc))
                .ToArray();
        }

        return Array.Empty<ControlRoomDecisionDto>();
    }

    private static ControlRoomMarketDto MapGammaMarket(GammaMarket market, int index)
    {
        var tokenIds = ParseTokenIds(market.ClobTokenIds);
        var outcomes = ParseStringArray(market.Outcomes);
        var prices = ParseDecimalArray(market.OutcomePrices);
        var yesPrice = prices.Count > 0 ? prices[0] : market.BestBid;
        var noPrice = prices.Count > 1 ? prices[1] : (decimal?)null;
        var volume24h = market.Volume24hrClob ?? market.Volume24hr ?? market.VolumeNum ?? 0m;
        var liquidity = market.LiquidityNum ?? 0m;

        return new ControlRoomMarketDto(
            market.Slug ?? market.Id,
            market.ConditionId,
            market.Question ?? market.Slug ?? market.Id,
            string.IsNullOrWhiteSpace(market.Category) ? "Prediction" : market.Category!,
            market.Closed == true ? "Closed" : market.Active == false ? "Paused" : "Active",
            yesPrice,
            noPrice,
            liquidity,
            volume24h,
            ParseDateTimeOffset(market.EndDateIso),
            CalculateSignalScore(liquidity, volume24h, index),
            market.Slug,
            market.Description,
            market.AcceptingOrders != false && market.Active != false && market.Closed != true,
            tokenIds.Select((tokenId, tokenIndex) => new ControlRoomMarketTokenDto(
                    tokenId,
                    tokenIndex < outcomes.Count ? outcomes[tokenIndex] : $"Outcome {tokenIndex + 1}",
                    tokenIndex < prices.Count ? prices[tokenIndex] : null,
                    null))
                .ToArray(),
            Array.Empty<string>(),
            market.Spread,
            "LiveGamma",
            0m,
            "Unranked",
            Array.Empty<string>());
    }

    private static ControlRoomMarketDto MapCatalogMarket(MarketInfoDto market, int index)
    {
        return new ControlRoomMarketDto(
            market.MarketId,
            market.ConditionId,
            market.Name,
            string.IsNullOrWhiteSpace(market.Category) ? "Prediction" : market.Category!,
            market.Status,
            null,
            null,
            market.Liquidity,
            market.Volume24h,
            market.ExpiresAtUtc,
            CalculateSignalScore(market.Liquidity, market.Volume24h, index),
            market.Slug,
            null,
            string.Equals(market.Status, "Active", StringComparison.OrdinalIgnoreCase),
            market.TokenIds.Select((tokenId, tokenIndex) => new ControlRoomMarketTokenDto(
                tokenId,
                tokenIndex == 0 ? "Yes" : tokenIndex == 1 ? "No" : $"Outcome {tokenIndex + 1}",
                null,
                null))
                .ToArray(),
            Array.Empty<string>(),
            null,
            "MarketCatalog",
            0m,
            "Unranked",
            Array.Empty<string>());
    }

    private ControlRoomOrderBookDto BuildOrderBook(
        ControlRoomMarketDto market,
        ControlRoomMarketTokenDto token,
        IEnumerable<(decimal Price, decimal Size)> bids,
        IEnumerable<(decimal Price, decimal Size)> asks,
        DateTimeOffset lastUpdatedUtc,
        DateTimeOffset observedAtUtc,
        string source)
    {
        var bidRows = bids
            .Where(level => level.Price > 0m && level.Size > 0m)
            .OrderByDescending(level => level.Price)
            .ToArray();
        var askRows = asks
            .Where(level => level.Price > 0m && level.Size > 0m)
            .OrderBy(level => level.Price)
            .ToArray();
        var maxNotional = bidRows.Concat(askRows)
            .Select(level => level.Price * level.Size)
            .DefaultIfEmpty(1m)
            .Max();
        var mappedBids = MapLevels(bidRows, maxNotional);
        var mappedAsks = MapLevels(askRows, maxNotional);
        var bestBid = mappedBids.FirstOrDefault();
        var bestAsk = mappedAsks.FirstOrDefault();
        var totalBid = mappedBids.Sum(level => level.Size);
        var totalAsk = mappedAsks.Sum(level => level.Size);
        var spread = bestBid is not null && bestAsk is not null ? bestAsk.Price - bestBid.Price : (decimal?)null;
        var midpoint = bestBid is not null && bestAsk is not null ? (bestAsk.Price + bestBid.Price) / 2m : (decimal?)null;
        var imbalance = totalBid + totalAsk <= 0m ? 0m : (totalBid - totalAsk) / (totalBid + totalAsk) * 100m;
        var options = controlRoomOptions.CurrentValue;
        var freshness = ControlRoomOrderBookFreshness.Evaluate(
            lastUpdatedUtc,
            observedAtUtc,
            options.OrderBookFreshSeconds,
            options.OrderBookStaleSeconds);

        return new ControlRoomOrderBookDto(
            market.MarketId,
            token.TokenId,
            token.Outcome,
            lastUpdatedUtc,
            bestBid?.Price,
            bestBid?.Size,
            bestAsk?.Price,
            bestAsk?.Size,
            spread,
            midpoint,
            totalBid,
            totalAsk,
            Math.Round(imbalance, 2),
            maxNotional,
            source,
            freshness,
            mappedBids,
            mappedAsks);
    }

    private static IReadOnlyList<ControlRoomOrderBookLevelDto> MapLevels(
        IReadOnlyList<(decimal Price, decimal Size)> levels,
        decimal maxNotional)
    {
        return levels
            .Select((level, index) =>
            {
                var notional = level.Price * level.Size;
                var depthPct = maxNotional <= 0m ? 0m : notional / maxNotional * 100m;
                return new ControlRoomOrderBookLevelDto(
                    index + 1,
                    Math.Round(level.Price, 4),
                    Math.Round(level.Size, 2),
                    Math.Round(notional, 2),
                    Math.Round(depthPct, 2));
            })
            .ToArray();
    }

    private static IReadOnlyList<ControlRoomMetricDto> BuildMicrostructureMetrics(
        ControlRoomOrderBookDto orderBook,
        ControlRoomMarketDto market)
    {
        return
        [
            new("Best bid", orderBook.BestBidPrice?.ToString("0.000") ?? "-", $"{orderBook.BestBidSize:0.##} shares", "neutral"),
            new("Best ask", orderBook.BestAskPrice?.ToString("0.000") ?? "-", $"{orderBook.BestAskSize:0.##} shares", "neutral"),
            new("Spread", orderBook.Spread?.ToString("0.000") ?? "-", orderBook.Source, orderBook.Spread > 0.03m ? "watch" : "good"),
            new("Depth imbalance", $"{orderBook.ImbalancePct:0.0}%", "bid minus ask", Math.Abs(orderBook.ImbalancePct) > 35m ? "watch" : "neutral"),
            new("Book freshness", orderBook.Freshness.Status, orderBook.Freshness.Message, orderBook.Freshness.Status == ControlRoomOrderBookFreshness.Fresh ? "good" : "watch"),
            new("Signal score", $"{market.SignalScore:P0}", $"{market.Volume24h:0} 24h volume", market.SignalScore > 0.7m ? "good" : "neutral")
        ];
    }

    private static ControlRoomMarketTokenDto? ResolveToken(
        ControlRoomMarketDto market,
        string? tokenId,
        string? outcome)
    {
        if (!string.IsNullOrWhiteSpace(tokenId))
        {
            return market.Tokens.FirstOrDefault(token =>
                string.Equals(token.TokenId, tokenId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(outcome))
        {
            var byOutcome = market.Tokens.FirstOrDefault(token =>
                string.Equals(token.Outcome, outcome, StringComparison.OrdinalIgnoreCase));
            if (byOutcome is not null)
            {
                return byOutcome;
            }
        }

        return market.Tokens.FirstOrDefault();
    }

    private static bool IsMarketMatch(ControlRoomMarketDto market, string marketId)
    {
        return string.Equals(market.MarketId, marketId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(market.ConditionId, marketId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(market.Slug, marketId, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ParseTokenIds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(raw) ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }
    }

    private static IReadOnlyList<string> ParseStringArray(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(raw) ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }
    }

    private static IReadOnlyList<decimal> ParseDecimalArray(string? raw)
    {
        return ParseStringArray(raw)
            .Select(item => decimal.TryParse(item, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                ? (decimal?)value
                : null)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToArray();
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? raw)
    {
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : null;
    }

    private static DateTimeOffset ParseTimestamp(string? raw)
    {
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
        {
            return unix > 9_999_999_999
                ? DateTimeOffset.FromUnixTimeMilliseconds(unix)
                : DateTimeOffset.FromUnixTimeSeconds(unix);
        }

        return ParseDateTimeOffset(raw) ?? DateTimeOffset.UtcNow;
    }

    private static decimal ParseDecimal(string? raw)
    {
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0m;
    }

    private static decimal CalculateSignalScore(decimal liquidity, decimal volume24h, int index)
    {
        var liquidityScore = Math.Clamp(liquidity / 50_000m, 0m, 0.45m);
        var volumeScore = Math.Clamp(volume24h / 100_000m, 0m, 0.45m);
        var freshnessScore = Math.Max(0m, 0.1m - index * 0.002m);
        return Math.Round(Math.Clamp(liquidityScore + volumeScore + freshnessScore, 0m, 1m), 2);
    }

    private static bool LooksLikePolymarketToken(string tokenId)
    {
        return tokenId.Length > 20 && tokenId.All(char.IsDigit);
    }

    private static string ResolveSource(IReadOnlyList<ControlRoomMarketDto> markets)
    {
        if (markets.Any(market => string.Equals(market.Source, "LiveGamma", StringComparison.OrdinalIgnoreCase)))
        {
            return "LiveGamma";
        }

        if (markets.Any(market => string.Equals(market.Source, "MarketCatalog", StringComparison.OrdinalIgnoreCase)))
        {
            return "MarketCatalog";
        }

        return "Unavailable";
    }

    private sealed class MarketUniverseState
    {
        public SemaphoreSlim LoadGate { get; } = new(1, 1);

        public List<ControlRoomMarketDto> Markets { get; } = [];

        public HashSet<string> SeenMarketIds { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int NextOffset { get; set; }

        public bool IsComplete { get; set; }
    }

    private sealed record MarketUniverseSnapshot(
        IReadOnlyList<ControlRoomMarketDto> Markets,
        bool IsComplete);
}
