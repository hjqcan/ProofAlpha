using Autotrade.MarketData.Application.Contract.Tape;
using Autotrade.MarketData.Application.Tape;
using Autotrade.MarketData.Infra.Data.Repositories;
using Autotrade.Testing.Db;
using Microsoft.EntityFrameworkCore;

namespace Autotrade.MarketData.Tests;

public sealed class MarketTapeRepositoryTests
{
    [Fact]
    public async Task AppendOrderBookTopTicksAsync_DeduplicatesAndReturnsAscending()
    {
        await using var db = new SqliteInMemoryDatabase();
        await using var context = TestDbContextFactory.CreateMarketDataContextSqlite(db.Connection);
        await context.Database.EnsureCreatedAsync();
        var repository = new EfMarketTapeRepository(context);
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await repository.AppendOrderBookTopTicksAsync(
            new[]
            {
                CreateTopTick(now.AddSeconds(10), "seq-2", 0.50m, 0.52m),
                CreateTopTick(now, "seq-1", 0.48m, 0.51m),
                CreateTopTick(now, "seq-1", 0.48m, 0.51m)
            });
        await repository.AppendOrderBookTopTicksAsync(
            new[] { CreateTopTick(now, "seq-1", 0.48m, 0.51m) });

        var ticks = await repository.GetTopTicksAsync(new MarketTapeQuery("market-1", "token-1"));

        Assert.Equal(2, ticks.Count);
        Assert.Equal(now, ticks[0].TimestampUtc);
        Assert.Equal(now.AddSeconds(10), ticks[1].TimestampUtc);
    }

    [Fact]
    public async Task GetReplaySliceAsync_ClampsResultsToAsOfUtc()
    {
        await using var db = new SqliteInMemoryDatabase();
        await using var context = TestDbContextFactory.CreateMarketDataContextSqlite(db.Connection);
        await context.Database.EnsureCreatedAsync();
        var repository = new EfMarketTapeRepository(context);
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await repository.AppendOrderBookTopTicksAsync(
            new[]
            {
                CreateTopTick(now, "seq-1", 0.48m, 0.51m),
                CreateTopTick(now.AddSeconds(10), "seq-2", 0.50m, 0.52m),
                CreateTopTick(now.AddSeconds(20), "seq-3", 0.60m, 0.63m)
            });

        var replay = await repository.GetReplaySliceAsync(
            new MarketTapeQuery(
                "market-1",
                "token-1",
                FromUtc: now,
                ToUtc: now.AddSeconds(20),
                AsOfUtc: now.AddSeconds(10)));

        Assert.Equal(2, replay.TopTicks.Count);
        Assert.All(replay.TopTicks, tick => Assert.True(tick.TimestampUtc <= now.AddSeconds(10)));
        Assert.Contains(replay.CompletenessNotes, note => note.Contains("clamped", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MarketReplayBacktestRunner_ProducesDeterministicTopOfBookBacktest()
    {
        await using var db = new SqliteInMemoryDatabase();
        await using var context = TestDbContextFactory.CreateMarketDataContextSqlite(db.Connection);
        await context.Database.EnsureCreatedAsync();
        var repository = new EfMarketTapeRepository(context);
        var runner = new MarketReplayBacktestRunner(repository);
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await repository.AppendOrderBookTopTicksAsync(
            new[]
            {
                CreateTopTick(now, "seq-1", 0.48m, 0.51m),
                CreateTopTick(now.AddSeconds(10), "seq-2", 0.55m, 0.57m),
                CreateTopTick(now.AddSeconds(20), "seq-3", 0.62m, 0.64m)
            });

        var request = new MarketReplayBacktestRequest(
            "market-1",
            "token-1",
            EntryMaxPrice: 0.52m,
            TakeProfitPrice: 0.60m,
            StopLossPrice: 0.45m,
            Quantity: 10m,
            MaxNotional: 6m,
            FromUtc: now,
            ToUtc: now.AddSeconds(30),
            AsOfUtc: now.AddSeconds(30));

        var first = await runner.RunAsync(request);
        var second = await runner.RunAsync(request);

        Assert.Equal(first.ReplaySeed, second.ReplaySeed);
        Assert.True(first.Entered);
        Assert.True(first.Exited);
        Assert.Equal(0.51m, first.Entry!.Price);
        Assert.Equal(0.62m, first.Exit!.Price);
        Assert.True(first.NetPnl > 0m);
    }

    private static OrderBookTopTickDto CreateTopTick(
        DateTimeOffset timestampUtc,
        string sourceSequence,
        decimal bid,
        decimal ask)
        => new(
            Guid.Empty,
            "market-1",
            "token-1",
            timestampUtc,
            bid,
            100m,
            ask,
            100m,
            ask - bid,
            "test",
            sourceSequence,
            "{}",
            timestampUtc);
}
