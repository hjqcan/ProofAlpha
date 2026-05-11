using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Engine;
using Xunit;

namespace Autotrade.Strategy.Tests.Engine;

public sealed class StrategyMarketChannelTests
{
    [Fact]
    public void TryWrite_AddsSnapshotToChannel()
    {
        using var channel = new StrategyMarketChannel(capacity: 10);
        var snapshot = CreateSnapshot("mkt-1");

        var result = channel.TryWrite(snapshot);

        Assert.True(result);
        Assert.Equal(1, channel.Backlog);
    }

    [Fact]
    public void TryWrite_WhenFull_DropsOldest()
    {
        using var channel = new StrategyMarketChannel(capacity: 2);

        channel.TryWrite(CreateSnapshot("mkt-1"));
        channel.TryWrite(CreateSnapshot("mkt-2"));
        channel.TryWrite(CreateSnapshot("mkt-3")); // Should drop mkt-1

        Assert.Equal(2, channel.Backlog);

        var batch = channel.TryReadBatch(10);
        Assert.Equal(2, batch.Count);
        Assert.Equal("mkt-2", batch[0].MarketId);
        Assert.Equal("mkt-3", batch[1].MarketId);
    }

    [Fact]
    public void TryWrite_SameMarket_CoalescesToLatestSnapshot()
    {
        using var channel = new StrategyMarketChannel(capacity: 10);
        var older = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var newer = older.AddSeconds(5);

        Assert.True(channel.TryWrite(CreateSnapshot("mkt-1", "Old Market", older)));
        Assert.True(channel.TryWrite(CreateSnapshot("mkt-1", "New Market", newer)));

        Assert.Equal(1, channel.Backlog);

        var batch = channel.TryReadBatch(10);

        var snapshot = Assert.Single(batch);
        Assert.Equal("mkt-1", snapshot.MarketId);
        Assert.Equal("New Market", snapshot.Market.Name);
        Assert.Equal(newer, snapshot.TimestampUtc);
        Assert.Equal(0, channel.Backlog);
    }

    [Fact]
    public void TryReadBatch_ReturnsAllAvailable()
    {
        using var channel = new StrategyMarketChannel(capacity: 10);

        channel.TryWrite(CreateSnapshot("mkt-1"));
        channel.TryWrite(CreateSnapshot("mkt-2"));
        channel.TryWrite(CreateSnapshot("mkt-3"));

        var batch = channel.TryReadBatch(maxCount: 2);

        Assert.Equal(2, batch.Count);
        Assert.Equal(1, channel.Backlog);
    }

    [Fact]
    public async Task ReadBatchAsync_WaitsForItems()
    {
        using var channel = new StrategyMarketChannel(capacity: 10);
        using var cts = new CancellationTokenSource();

        var readTask = Task.Run(async () =>
        {
            return await channel.ReadBatchAsync(5, cts.Token);
        });

        await Task.Delay(50);

        channel.TryWrite(CreateSnapshot("mkt-1"));
        channel.TryWrite(CreateSnapshot("mkt-2"));

        var batch = await readTask;

        Assert.Equal(2, batch.Count);
    }

    [Fact]
    public void WriteAll_WritesMultipleSnapshots()
    {
        using var channel = new StrategyMarketChannel(capacity: 10);

        var snapshots = new[]
        {
            CreateSnapshot("mkt-1"),
            CreateSnapshot("mkt-2"),
            CreateSnapshot("mkt-3")
        };

        var written = channel.WriteAll(snapshots);

        Assert.Equal(3, written);
        Assert.Equal(3, channel.Backlog);
    }

    [Fact]
    public void Dispose_CompletesChannel()
    {
        var channel = new StrategyMarketChannel(capacity: 10);
        channel.TryWrite(CreateSnapshot("mkt-1"));

        channel.Dispose();

        Assert.False(channel.TryWrite(CreateSnapshot("mkt-2")));
    }

    [Fact]
    public void Constructor_ThrowsOnInvalidCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StrategyMarketChannel(capacity: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StrategyMarketChannel(capacity: -1));
    }

    private static MarketSnapshot CreateSnapshot(
        string marketId,
        string name = "Test Market",
        DateTimeOffset? timestampUtc = null)
    {
        var market = new MarketInfoDto
        {
            MarketId = marketId,
            ConditionId = "cond-1",
            Name = name,
            Status = "Active",
            TokenIds = new[] { "yes-token", "no-token" }
        };

        return new MarketSnapshot(market, null, null, timestampUtc ?? DateTimeOffset.UtcNow);
    }
}
