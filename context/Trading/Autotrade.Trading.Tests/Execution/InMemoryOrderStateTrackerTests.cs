using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Execution;
using Microsoft.Extensions.Logging;
using Moq;

namespace Autotrade.Trading.Tests.Execution;

public class InMemoryOrderStateTrackerTests
{
    private readonly InMemoryOrderStateTracker _tracker;

    public InMemoryOrderStateTrackerTests()
    {
        var loggerMock = new Mock<ILogger<InMemoryOrderStateTracker>>();
        _tracker = new InMemoryOrderStateTracker(loggerMock.Object);
    }

    [Fact]
    public async Task OnOrderStateChangedAsync_新挂单_应增加计数()
    {
        var update = new OrderStateUpdate
        {
            ClientOrderId = "order-1",
            ExchangeOrderId = "EX-001",
            MarketId = "market-1",
            Status = ExecutionStatus.Accepted,
            OriginalQuantity = 100m,
            FilledQuantity = 0m
        };

        await _tracker.OnOrderStateChangedAsync(update);

        Assert.Equal(1, _tracker.GetOpenOrderCount("market-1"));
    }

    [Fact]
    public async Task OnOrderStateChangedAsync_订单完成_应减少计数()
    {
        // 先创建挂单
        await _tracker.OnOrderStateChangedAsync(new OrderStateUpdate
        {
            ClientOrderId = "order-1",
            ExchangeOrderId = "EX-001",
            MarketId = "market-1",
            Status = ExecutionStatus.Accepted,
            OriginalQuantity = 100m,
            FilledQuantity = 0m
        });

        Assert.Equal(1, _tracker.GetOpenOrderCount("market-1"));

        // 订单完成
        await _tracker.OnOrderStateChangedAsync(new OrderStateUpdate
        {
            ClientOrderId = "order-1",
            ExchangeOrderId = "EX-001",
            MarketId = "market-1",
            Status = ExecutionStatus.Filled,
            OriginalQuantity = 100m,
            FilledQuantity = 100m
        });

        Assert.Equal(0, _tracker.GetOpenOrderCount("market-1"));
    }

    [Fact]
    public async Task OnOrderStateChangedAsync_订单取消_应减少计数()
    {
        await _tracker.OnOrderStateChangedAsync(new OrderStateUpdate
        {
            ClientOrderId = "order-1",
            ExchangeOrderId = "EX-001",
            MarketId = "market-1",
            Status = ExecutionStatus.Accepted,
            OriginalQuantity = 100m,
            FilledQuantity = 0m
        });

        await _tracker.OnOrderStateChangedAsync(new OrderStateUpdate
        {
            ClientOrderId = "order-1",
            ExchangeOrderId = "EX-001",
            MarketId = "market-1",
            Status = ExecutionStatus.Cancelled,
            OriginalQuantity = 100m,
            FilledQuantity = 0m
        });

        Assert.Equal(0, _tracker.GetOpenOrderCount("market-1"));
    }

    [Fact]
    public async Task OnOrderStateChangedAsync_多个订单_应分别计数()
    {
        await _tracker.OnOrderStateChangedAsync(new OrderStateUpdate
        {
            ClientOrderId = "order-1",
            ExchangeOrderId = "EX-001",
            MarketId = "market-1",
            Status = ExecutionStatus.Accepted,
            OriginalQuantity = 100m,
            FilledQuantity = 0m
        });

        await _tracker.OnOrderStateChangedAsync(new OrderStateUpdate
        {
            ClientOrderId = "order-2",
            ExchangeOrderId = "EX-002",
            MarketId = "market-1",
            Status = ExecutionStatus.Accepted,
            OriginalQuantity = 50m,
            FilledQuantity = 0m
        });

        await _tracker.OnOrderStateChangedAsync(new OrderStateUpdate
        {
            ClientOrderId = "order-3",
            ExchangeOrderId = "EX-003",
            MarketId = "market-2",
            Status = ExecutionStatus.Accepted,
            OriginalQuantity = 200m,
            FilledQuantity = 0m
        });

        Assert.Equal(2, _tracker.GetOpenOrderCount("market-1"));
        Assert.Equal(1, _tracker.GetOpenOrderCount("market-2"));
    }

    [Fact]
    public void GetOpenOrderCount_不存在市场_应返回0()
    {
        Assert.Equal(0, _tracker.GetOpenOrderCount("non-existent"));
    }

    [Fact]
    public async Task GetAllOpenOrderCounts_应返回所有市场计数()
    {
        await _tracker.OnOrderStateChangedAsync(new OrderStateUpdate
        {
            ClientOrderId = "order-1",
            ExchangeOrderId = "EX-001",
            MarketId = "market-1",
            Status = ExecutionStatus.Accepted,
            OriginalQuantity = 100m,
            FilledQuantity = 0m
        });

        await _tracker.OnOrderStateChangedAsync(new OrderStateUpdate
        {
            ClientOrderId = "order-2",
            ExchangeOrderId = "EX-002",
            MarketId = "market-2",
            Status = ExecutionStatus.Accepted,
            OriginalQuantity = 50m,
            FilledQuantity = 0m
        });

        var counts = _tracker.GetAllOpenOrderCounts();

        Assert.Equal(2, counts.Count);
        Assert.Equal(1, counts["market-1"]);
        Assert.Equal(1, counts["market-2"]);
    }

    [Fact]
    public async Task GetOrderState_应返回订单状态()
    {
        var update = new OrderStateUpdate
        {
            ClientOrderId = "order-1",
            ExchangeOrderId = "EX-001",
            MarketId = "market-1",
            Status = ExecutionStatus.PartiallyFilled,
            OriginalQuantity = 100m,
            FilledQuantity = 30m
        };

        await _tracker.OnOrderStateChangedAsync(update);

        var state = _tracker.GetOrderState("order-1");

        Assert.NotNull(state);
        Assert.Equal(ExecutionStatus.PartiallyFilled, state.Status);
        Assert.Equal(30m, state.FilledQuantity);
    }

    [Fact]
    public async Task GetOpenOrders_应只返回挂单状态()
    {
        await _tracker.OnOrderStateChangedAsync(new OrderStateUpdate
        {
            ClientOrderId = "order-open",
            ExchangeOrderId = "EX-OPEN",
            MarketId = "market-1",
            Status = ExecutionStatus.Accepted,
            OriginalQuantity = 10m,
            FilledQuantity = 0m
        });

        await _tracker.OnOrderStateChangedAsync(new OrderStateUpdate
        {
            ClientOrderId = "order-closed",
            ExchangeOrderId = "EX-CLOSED",
            MarketId = "market-1",
            Status = ExecutionStatus.Filled,
            OriginalQuantity = 10m,
            FilledQuantity = 10m
        });

        var openOrders = _tracker.GetOpenOrders();

        Assert.Single(openOrders);
        Assert.Equal("order-open", openOrders[0].ClientOrderId);
    }

    [Fact]
    public async Task Clear_应清空所有数据()
    {
        await _tracker.OnOrderStateChangedAsync(new OrderStateUpdate
        {
            ClientOrderId = "order-1",
            ExchangeOrderId = "EX-001",
            MarketId = "market-1",
            Status = ExecutionStatus.Accepted,
            OriginalQuantity = 100m,
            FilledQuantity = 0m
        });

        _tracker.Clear();

        Assert.Equal(0, _tracker.GetOpenOrderCount("market-1"));
        Assert.Null(_tracker.GetOrderState("order-1"));
    }
}
