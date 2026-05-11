using Autotrade.Trading.Domain.Entities;
using Autotrade.Trading.Domain.Shared.Enums;
using Autotrade.Trading.Domain.Shared.ValueObjects;

namespace Autotrade.Trading.Tests;

public class PriceAndQuantityInvariantTests
{
    [Fact]
    public void Price_超出区间_应抛异常()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Price(-0.01m));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Price(1.01m));
    }

    [Fact]
    public void Quantity_负数_应抛异常()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Quantity(-1m));
    }
}

public class TradingAccountInvariantTests
{
    [Fact]
    public void TradingAccount_创建参数非法_应抛异常()
    {
        Assert.Throws<ArgumentException>(() => new TradingAccount(" ", 0m, 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TradingAccount("w", -1m, 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TradingAccount("w", 1m, -1m));
        Assert.Throws<ArgumentException>(() => new TradingAccount("w", totalCapital: 10m, availableCapital: 11m));
    }

    [Fact]
    public void TradingAccount_Debit_Credit_应更新余额()
    {
        var a = new TradingAccount("w", totalCapital: 100m, availableCapital: 50m);

        a.Debit(10m);
        Assert.Equal(40m, a.AvailableCapital);
        Assert.Equal(100m, a.TotalCapital);

        a.Credit(5m);
        Assert.Equal(45m, a.AvailableCapital);
        Assert.Equal(105m, a.TotalCapital);
    }

    [Fact]
    public void TradingAccount_Debit_超过可用资金_应抛异常()
    {
        var a = new TradingAccount("w", totalCapital: 100m, availableCapital: 10m);
        Assert.Throws<InvalidOperationException>(() => a.Debit(11m));
    }
}

public class OrderStateMachineTests
{
    [Fact]
    public void Order_创建参数非法_应抛异常()
    {
        var aggId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() =>
            new Order(Guid.Empty, "m", OutcomeSide.Yes, OrderSide.Buy, OrderType.Limit, TimeInForce.Gtc, new Price(0.5m), new Quantity(1m)));

        Assert.Throws<ArgumentException>(() =>
            new Order(aggId, " ", OutcomeSide.Yes, OrderSide.Buy, OrderType.Limit, TimeInForce.Gtc, new Price(0.5m), new Quantity(1m)));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Order(aggId, "m", OutcomeSide.Yes, OrderSide.Buy, OrderType.Limit, TimeInForce.Gtc, new Price(0.5m), new Quantity(0m)));

        Assert.Throws<ArgumentException>(() =>
            new Order(aggId, "m", OutcomeSide.Yes, OrderSide.Buy, OrderType.Limit, TimeInForce.Gtd, new Price(0.5m), new Quantity(1m), goodTilDateUtc: null));
    }

    [Fact]
    public void Order_状态机_应符合预期()
    {
        var o = new Order(
            tradingAccountId: Guid.NewGuid(),
            marketId: "m",
            outcome: OutcomeSide.Yes,
            side: OrderSide.Buy,
            orderType: OrderType.Limit,
            timeInForce: TimeInForce.Gtc,
            price: new Price(0.4m),
            quantity: new Quantity(10m));

        Assert.Equal(OrderStatus.Pending, o.Status);

        o.MarkOpen();
        Assert.Equal(OrderStatus.Open, o.Status);

        o.ApplyFill(new Quantity(3m), 0.4m);
        Assert.Equal(OrderStatus.PartiallyFilled, o.Status);
        Assert.Equal(3m, o.FilledQuantity.Value);

        o.ApplyFill(new Quantity(7m), 0.4m);
        Assert.Equal(OrderStatus.Filled, o.Status);
        Assert.Equal(10m, o.FilledQuantity.Value);
    }

    [Fact]
    public void Order_取消已成交_应抛异常()
    {
        var o = new Order(
            tradingAccountId: Guid.NewGuid(),
            marketId: "m",
            outcome: OutcomeSide.Yes,
            side: OrderSide.Buy,
            orderType: OrderType.Limit,
            timeInForce: TimeInForce.Gtc,
            price: new Price(0.4m),
            quantity: new Quantity(1m));

        o.ApplyFill(new Quantity(1m), 0.4m);
        Assert.Equal(OrderStatus.Filled, o.Status);

        Assert.Throws<InvalidOperationException>(() => o.Cancel());
    }
}

public class PositionInvariantTests
{
    [Fact]
    public void Position_创建参数非法_应抛异常()
    {
        Assert.Throws<ArgumentException>(() => new Position(Guid.Empty, "m", OutcomeSide.Yes));
        Assert.Throws<ArgumentException>(() => new Position(Guid.NewGuid(), " ", OutcomeSide.Yes));
    }

    [Fact]
    public void Position_买入卖出_应更新数量均价与已实现盈亏()
    {
        var p = new Position(Guid.NewGuid(), "m", OutcomeSide.Yes);

        p.ApplyBuy(new Quantity(10m), new Price(0.4m));
        Assert.Equal(10m, p.Quantity.Value);
        Assert.Equal(0.4m, p.AverageCost.Value);

        p.ApplyBuy(new Quantity(10m), new Price(0.6m));
        Assert.Equal(20m, p.Quantity.Value);
        Assert.Equal(0.5m, p.AverageCost.Value);

        p.ApplySell(new Quantity(5m), new Price(0.7m));
        Assert.Equal(15m, p.Quantity.Value);
        Assert.True(p.RealizedPnl > 0m);

        p.ApplySell(new Quantity(15m), new Price(0.5m));
        Assert.Equal(0m, p.Quantity.Value);
        Assert.Equal(0m, p.AverageCost.Value);
    }
}

public class RiskEventAndAggregateTests
{
    [Fact]
    public void RiskEvent_创建参数非法_应抛异常()
    {
        Assert.Throws<ArgumentException>(() => new RiskEvent(Guid.Empty, "c", RiskSeverity.Info, "m"));
        Assert.Throws<ArgumentException>(() => new RiskEvent(Guid.NewGuid(), " ", RiskSeverity.Info, "m"));
        Assert.Throws<ArgumentException>(() => new RiskEvent(Guid.NewGuid(), "c", RiskSeverity.Info, " "));
    }

    [Fact]
    public void TradingAccount_RecordRiskEvent_应追加事件()
    {
        var account = new TradingAccount(walletAddress: "w", totalCapital: 1m, availableCapital: 1m);
        Assert.Empty(account.RiskEvents);

        account.RecordRiskEvent("CODE", RiskSeverity.Warning, "msg", "{\"k\":\"v\"}");
        Assert.Single(account.RiskEvents);
        Assert.Equal("CODE", account.RiskEvents[0].Code);
    }
}

