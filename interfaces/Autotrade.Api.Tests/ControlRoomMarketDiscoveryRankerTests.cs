using Autotrade.Api.ControlRoom;

namespace Autotrade.Api.Tests;

public sealed class ControlRoomMarketDiscoveryRankerTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RankSortIsDeterministicAndPenalizesUnsuitableMarkets()
    {
        var markets = new[]
        {
            CreateMarket(
                "blocked-high-volume",
                status: "Paused",
                acceptingOrders: false,
                liquidity: 250_000m,
                volume24h: 240_000m,
                signalScore: 0.95m,
                spread: 0.02m),
            CreateMarket(
                "best-tradable",
                liquidity: 100_000m,
                volume24h: 80_000m,
                signalScore: 0.82m,
                spread: 0.02m),
            CreateMarket(
                "usable-tradable",
                liquidity: 5_000m,
                volume24h: 1_500m,
                signalScore: 0.48m,
                spread: 0.04m)
        };

        var enriched = ControlRoomMarketDiscoveryRanker.EnrichMarkets(markets, Now);
        var ranked = ControlRoomMarketDiscoveryRanker.SortMarkets(enriched, sort: "rank");

        Assert.Equal(["best-tradable", "usable-tradable", "blocked-high-volume"], ranked.Select(market => market.MarketId));
        Assert.True(ranked[0].RankScore > ranked[1].RankScore);
        Assert.Contains(ranked[2].UnsuitableReasons, reason => reason.Contains("not accepting orders", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DiscoveryFiltersCoverCategoryStatusLiquidityVolumeExpiryAcceptingAndSignal()
    {
        var markets = ControlRoomMarketDiscoveryRanker.EnrichMarkets(
            [
                CreateMarket("matching", category: "Politics", expiresAtUtc: Now.AddDays(12)),
                CreateMarket("wrong-category", category: "Sports", expiresAtUtc: Now.AddDays(12)),
                CreateMarket("low-liquidity", liquidity: 200m, expiresAtUtc: Now.AddDays(12)),
                CreateMarket("low-volume", volume24h: 100m, expiresAtUtc: Now.AddDays(12)),
                CreateMarket("too-distant", expiresAtUtc: Now.AddDays(90)),
                CreateMarket("not-accepting", acceptingOrders: false, expiresAtUtc: Now.AddDays(12)),
                CreateMarket("weak-signal", signalScore: 0.1m, expiresAtUtc: Now.AddDays(12))
            ],
            Now);
        var query = new ControlRoomMarketDiscoveryQuery(
            Category: "Politics",
            Status: "Active",
            MinLiquidity: 1_000m,
            MinVolume24h: 500m,
            MaxDaysToExpiry: 30,
            AcceptingOrders: true,
            MinSignalScore: 0.3m);

        var filtered = ControlRoomMarketDiscoveryRanker.FilterMarkets(markets, query, Now);

        var market = Assert.Single(filtered);
        Assert.Equal("matching", market.MarketId);
        Assert.Empty(market.UnsuitableReasons);
        Assert.Contains("accepting orders", market.RankReason, StringComparison.OrdinalIgnoreCase);
    }

    private static ControlRoomMarketDto CreateMarket(
        string marketId,
        string category = "Politics",
        string status = "Active",
        bool acceptingOrders = true,
        decimal liquidity = 25_000m,
        decimal volume24h = 3_500m,
        decimal signalScore = 0.62m,
        decimal? spread = 0.04m,
        DateTimeOffset? expiresAtUtc = null)
    {
        return new ControlRoomMarketDto(
            MarketId: marketId,
            ConditionId: $"condition-{marketId}",
            Name: $"Fixture market {marketId}",
            Category: category,
            Status: status,
            YesPrice: 0.48m,
            NoPrice: 0.52m,
            Liquidity: liquidity,
            Volume24h: volume24h,
            ExpiresAtUtc: expiresAtUtc ?? Now.AddDays(10),
            SignalScore: signalScore,
            Slug: marketId,
            Description: "Deterministic fixture market.",
            AcceptingOrders: acceptingOrders,
            Tokens:
            [
                new ControlRoomMarketTokenDto($"token-{marketId}-yes", "YES", 0.48m, null),
                new ControlRoomMarketTokenDto($"token-{marketId}-no", "NO", 0.52m, null)
            ],
            Tags:
            [
                "fixture",
                "ranking"
            ],
            Spread: spread,
            Source: "ranker-test",
            RankScore: 0m,
            RankReason: "Unranked",
            UnsuitableReasons: Array.Empty<string>());
    }
}
