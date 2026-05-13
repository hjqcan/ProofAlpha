using Autotrade.MarketData.Application.Contract.Tape;
using Autotrade.Strategy.Application.Strategies.DualLeg;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Strategy.Tests;

public sealed class DualLegArbitrageReplayRunnerTests
{
    [Fact]
    public async Task RunAsync_AcceptsDepthAwareTwoLegFill_WhenEdgeSurvivesFeesAndSlippage()
    {
        var now = new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero);
        var runner = new DualLegArbitrageReplayRunner(new StaticReplayReader(
            Slice("yes-token", Depth("yes-token", now, "yes-1", Ask(0.42m, 12m))),
            Slice("no-token", Depth("no-token", now, "no-1", Ask(0.53m, 12m)))));

        var result = await runner.RunAsync(Request(now));

        Assert.True(result.Accepted);
        Assert.Equal("accepted", result.Status);
        Assert.Equal(DualLegArbitrageReplayRunner.FillModelVersion, result.FillModelVersion);
        Assert.Equal(10m, result.Quantity);
        Assert.Equal(2, result.Fills.Count);
        Assert.All(result.Fills, fill => Assert.Equal(OrderSide.Buy, fill.Side));
        Assert.True(result.SlippageAdjustedPairCost + result.FeePerUnit < 0.99m);
        Assert.True(result.NetEdgeUsdc > 0m);
    }

    [Fact]
    public async Task RunAsync_RejectsFalseEdge_AfterFeesAndSlippage()
    {
        var now = new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero);
        var runner = new DualLegArbitrageReplayRunner(new StaticReplayReader(
            Slice("yes-token", Depth("yes-token", now, "yes-1", Ask(0.49m, 20m))),
            Slice("no-token", Depth("no-token", now, "no-1", Ask(0.49m, 20m)))));

        var result = await runner.RunAsync(Request(now) with
        {
            MaxSlippage = 0.02m,
            FeeRateBps = 20m,
            PairCostThreshold = 0.99m
        });

        Assert.False(result.Accepted);
        Assert.Equal("no_profitable_two_leg_fill", result.Status);
        Assert.Contains(result.RejectionReasons, reason => reason.Contains("Pair cost", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_RejectsOneLegOnlyDepth_WhenExecutableQuantityBelowMinimum()
    {
        var now = new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero);
        var runner = new DualLegArbitrageReplayRunner(new StaticReplayReader(
            Slice("yes-token", Depth("yes-token", now, "yes-1", Ask(0.42m, 12m))),
            Slice("no-token", Depth("no-token", now, "no-1", Ask(0.53m, 0.25m)))));

        var result = await runner.RunAsync(Request(now));

        Assert.False(result.Accepted);
        Assert.Contains(result.RejectionReasons, reason => reason.Contains("below min", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_RejectsStaleOppositeLeg()
    {
        var now = new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero);
        var runner = new DualLegArbitrageReplayRunner(new StaticReplayReader(
            Slice("yes-token", Depth("yes-token", now.AddSeconds(30), "yes-1", Ask(0.42m, 12m))),
            Slice("no-token", Depth("no-token", now, "no-1", Ask(0.53m, 12m)))));

        var result = await runner.RunAsync(Request(now) with
        {
            ToUtc = now.AddSeconds(30),
            AsOfUtc = now.AddSeconds(30),
            MaxQuoteAge = TimeSpan.FromSeconds(5)
        });

        Assert.False(result.Accepted);
        Assert.Contains(result.RejectionReasons, reason => reason.Contains("Quote age", StringComparison.OrdinalIgnoreCase));
    }

    private static DualLegArbitrageReplayRequest Request(DateTimeOffset now)
        => new(
            "market-1",
            "yes-token",
            "no-token",
            Quantity: 10m,
            MinOrderQuantity: 1m,
            MaxNotionalUsdc: 10m,
            PairCostThreshold: 0.99m,
            MaxSlippage: 0.002m,
            FeeRateBps: 10m,
            FromUtc: now,
            ToUtc: now.AddMinutes(5),
            AsOfUtc: now.AddMinutes(5),
            MaxQuoteAge: TimeSpan.FromSeconds(10));

    private static MarketTapeReplaySlice Slice(string tokenId, params OrderBookDepthSnapshotDto[] snapshots)
        => new(
            new MarketTapeQuery("market-1", tokenId),
            Array.Empty<MarketPriceTickDto>(),
            Array.Empty<OrderBookTopTickDto>(),
            snapshots,
            Array.Empty<ClobTradeTickDto>(),
            Array.Empty<MarketResolutionEventDto>(),
            Array.Empty<string>());

    private static OrderBookDepthSnapshotDto Depth(
        string tokenId,
        DateTimeOffset timestamp,
        string sequence,
        params OrderBookDepthLevelDto[] asks)
        => new(
            Guid.Empty,
            "market-1",
            tokenId,
            timestamp,
            sequence,
            Array.Empty<OrderBookDepthLevelDto>(),
            asks,
            "test",
            "{}",
            timestamp);

    private static OrderBookDepthLevelDto Ask(decimal price, decimal size)
        => new(price, size, IsBid: false);

    private sealed class StaticReplayReader : IMarketReplayReader
    {
        private readonly Dictionary<string, MarketTapeReplaySlice> _slices;

        public StaticReplayReader(params MarketTapeReplaySlice[] slices)
        {
            _slices = slices.ToDictionary(slice => slice.Query.TokenId!, StringComparer.OrdinalIgnoreCase);
        }

        public Task<MarketTapeReplaySlice> GetReplaySliceAsync(
            MarketTapeQuery query,
            CancellationToken cancellationToken = default)
        {
            if (query.TokenId is not null && _slices.TryGetValue(query.TokenId, out var slice))
            {
                var filteredDepth = slice.DepthSnapshots
                    .Where(snapshot => (!query.FromUtc.HasValue || snapshot.TimestampUtc >= query.FromUtc.Value) &&
                        (!query.ToUtc.HasValue || snapshot.TimestampUtc <= query.ToUtc.Value) &&
                        (!query.AsOfUtc.HasValue || snapshot.TimestampUtc <= query.AsOfUtc.Value))
                    .ToArray();

                return Task.FromResult(slice with { Query = query, DepthSnapshots = filteredDepth });
            }

            return Task.FromResult(new MarketTapeReplaySlice(
                query,
                Array.Empty<MarketPriceTickDto>(),
                Array.Empty<OrderBookTopTickDto>(),
                Array.Empty<OrderBookDepthSnapshotDto>(),
                Array.Empty<ClobTradeTickDto>(),
                Array.Empty<MarketResolutionEventDto>(),
                Array.Empty<string>()));
        }
    }
}
