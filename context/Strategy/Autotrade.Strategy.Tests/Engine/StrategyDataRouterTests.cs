using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Engine;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Autotrade.Strategy.Tests.Engine;

public sealed class StrategyDataRouterTests
{
    [Fact]
    public void RegisterStrategy_CreatesChannel()
    {
        using var router = CreateRouter();

        var channel = router.RegisterStrategy("strategy-1", channelCapacity: 50);

        Assert.NotNull(channel);
        Assert.Equal(50, channel.Capacity);
    }

    [Fact]
    public void RegisterStrategy_ReturnsSameChannel_WhenCalledTwice()
    {
        using var router = CreateRouter();

        var channel1 = router.RegisterStrategy("strategy-1");
        var channel2 = router.RegisterStrategy("strategy-1");

        Assert.Same(channel1, channel2);
    }

    [Fact]
    public void UnregisterStrategy_DisposesChannel()
    {
        using var router = CreateRouter();

        var channel = router.RegisterStrategy("strategy-1");
        router.UnregisterStrategy("strategy-1");

        Assert.False(channel.TryWrite(CreateSnapshot("mkt-1")));
    }

    [Fact]
    public void UpdateSubscriptions_UpdatesMarketList()
    {
        using var router = CreateRouter();
        router.RegisterStrategy("strategy-1");

        router.UpdateSubscriptions("strategy-1", new[] { "mkt-1", "mkt-2" });

        var subscriptions = router.GetSubscriptions("strategy-1");
        Assert.Equal(2, subscriptions.Count);
        Assert.Contains("mkt-1", subscriptions);
        Assert.Contains("mkt-2", subscriptions);
    }

    [Fact]
    public void GetSubscriptions_ReturnsEmpty_WhenNotRegistered()
    {
        using var router = CreateRouter();

        var subscriptions = router.GetSubscriptions("unknown-strategy");

        Assert.Empty(subscriptions);
    }

    [Fact]
    public void GetChannelBacklog_ReturnsCorrectCount()
    {
        using var router = CreateRouter();
        var channel = router.RegisterStrategy("strategy-1");

        channel.TryWrite(CreateSnapshot("mkt-1"));
        channel.TryWrite(CreateSnapshot("mkt-2"));

        Assert.Equal(2, router.GetChannelBacklog("strategy-1"));
    }

    [Fact]
    public void GetChannelBacklog_ReturnsZero_WhenNotRegistered()
    {
        using var router = CreateRouter();

        Assert.Equal(0, router.GetChannelBacklog("unknown-strategy"));
    }

    [Fact]
    public void Dispose_DisposesAllChannels()
    {
        var channel1 = default(StrategyMarketChannel);
        var channel2 = default(StrategyMarketChannel);

        using (var router = CreateRouter())
        {
            channel1 = router.RegisterStrategy("strategy-1");
            channel2 = router.RegisterStrategy("strategy-2");
        }

        Assert.False(channel1!.TryWrite(CreateSnapshot("mkt-1")));
        Assert.False(channel2!.TryWrite(CreateSnapshot("mkt-1")));
    }

    private static StrategyDataRouter CreateRouter()
    {
        var snapshotProvider = new FakeSnapshotProvider();
        return new StrategyDataRouter(snapshotProvider, NullLogger<StrategyDataRouter>.Instance);
    }

    private static MarketSnapshot CreateSnapshot(string marketId)
    {
        var market = new MarketInfoDto
        {
            MarketId = marketId,
            ConditionId = "cond-1",
            Name = "Test Market",
            Status = "Active",
            TokenIds = new[] { "yes-token", "no-token" }
        };

        return new MarketSnapshot(market, null, null, DateTimeOffset.UtcNow);
    }

    private sealed class FakeSnapshotProvider : IMarketSnapshotProvider
    {
        private readonly Dictionary<string, MarketSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);

        public void AddSnapshot(MarketSnapshot snapshot)
        {
            if (snapshot.MarketId is not null)
            {
                _snapshots[snapshot.MarketId] = snapshot;
            }
        }

        public Task<IReadOnlyList<MarketSnapshot>> GetSnapshotsAsync(
            IEnumerable<string> marketIds,
            CancellationToken cancellationToken = default)
        {
            var result = new List<MarketSnapshot>();
            foreach (var marketId in marketIds)
            {
                if (_snapshots.TryGetValue(marketId, out var snapshot))
                {
                    result.Add(snapshot);
                }
            }

            return Task.FromResult<IReadOnlyList<MarketSnapshot>>(result);
        }
    }
}
