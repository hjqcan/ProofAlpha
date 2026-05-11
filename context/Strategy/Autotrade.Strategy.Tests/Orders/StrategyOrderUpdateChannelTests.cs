using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Orders;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Xunit;

namespace Autotrade.Strategy.Tests.Orders;

public sealed class StrategyOrderUpdateChannelTests
{
    [Fact]
    public void TryWrite_EnqueuesUpdate_AndTryReadAllDrains()
    {
        using var channel = new StrategyOrderUpdateChannel(capacity: 10);
        var update = CreateUpdate("strategy-1", "cid-1");

        Assert.True(channel.TryWrite(update));
        Assert.Equal(1, channel.Backlog);

        var batch = channel.TryReadAll();
        Assert.Single(batch);
        Assert.Equal("cid-1", batch[0].ClientOrderId);
        Assert.Equal(0, channel.Backlog);
    }

    [Fact]
    public void TryWrite_AfterDispose_ReturnsFalse()
    {
        using var channel = new StrategyOrderUpdateChannel(capacity: 10);
        channel.Dispose();

        Assert.False(channel.TryWrite(CreateUpdate("strategy-1", "cid-1")));
    }

    [Fact]
    public async Task WriteAsync_AfterDispose_ReturnsFalse()
    {
        using var channel = new StrategyOrderUpdateChannel(capacity: 10);
        channel.Dispose();

        var ok = await channel.WriteAsync(CreateUpdate("strategy-1", "cid-1"), TimeSpan.FromMilliseconds(50), CancellationToken.None);
        Assert.False(ok);
    }

    [Fact]
    public void TryReadAll_AfterDispose_ReturnsEmpty()
    {
        using var channel = new StrategyOrderUpdateChannel(capacity: 10);
        channel.Dispose();

        Assert.Empty(channel.TryReadAll());
    }

    private static StrategyOrderUpdate CreateUpdate(string strategyId, string clientOrderId)
    {
        return new StrategyOrderUpdate(
            StrategyId: strategyId,
            ClientOrderId: clientOrderId,
            MarketId: "mkt-1",
            TokenId: "token-1",
            Outcome: OutcomeSide.Yes,
            Leg: OrderLeg.First,
            SignalType: StrategySignalType.Entry,
            Side: OrderSide.Buy,
            OrderType: OrderType.Limit,
            TimeInForce: TimeInForce.Gtc,
            Price: 0.5m,
            Status: ExecutionStatus.Accepted,
            FilledQuantity: 0m,
            OriginalQuantity: 1m,
            TimestampUtc: DateTimeOffset.UtcNow);
    }
}

