using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Execution;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Autotrade.Trading.Tests.Execution;

public class OrderLimitValidatorTests
{
    private readonly Mock<IOrderStateTracker> _stateTrackerMock;
    private readonly OrderLimitValidator _validator;

    public OrderLimitValidatorTests()
    {
        _stateTrackerMock = new Mock<IOrderStateTracker>();

        var options = Options.Create(new ExecutionOptions
        {
            MaxOpenOrdersPerMarket = 5
        });

        _validator = new OrderLimitValidator(_stateTrackerMock.Object, options);
    }

    [Fact]
    public void ValidateCanPlaceOrder_未达到限制_应返回Null()
    {
        _stateTrackerMock.Setup(s => s.GetOpenOrderCount("market-1")).Returns(3);

        var result = _validator.ValidateCanPlaceOrder("market-1");

        Assert.Null(result);
    }

    [Fact]
    public void ValidateCanPlaceOrder_达到限制_应返回错误()
    {
        _stateTrackerMock.Setup(s => s.GetOpenOrderCount("market-1")).Returns(5);

        var result = _validator.ValidateCanPlaceOrder("market-1");

        Assert.NotNull(result);
        Assert.Contains("5/5", result);
    }

    [Fact]
    public void ValidateCanPlaceOrder_超过限制_应返回错误()
    {
        _stateTrackerMock.Setup(s => s.GetOpenOrderCount("market-1")).Returns(6);

        var result = _validator.ValidateCanPlaceOrder("market-1");

        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateCanPlaceOrder_空MarketId_应返回Null()
    {
        var result = _validator.ValidateCanPlaceOrder("");

        Assert.Null(result);
    }

    [Fact]
    public void GetRemainingOrderSlots_有剩余_应返回正确数量()
    {
        _stateTrackerMock.Setup(s => s.GetOpenOrderCount("market-1")).Returns(2);

        var result = _validator.GetRemainingOrderSlots("market-1");

        Assert.Equal(3, result);
    }

    [Fact]
    public void GetRemainingOrderSlots_无剩余_应返回0()
    {
        _stateTrackerMock.Setup(s => s.GetOpenOrderCount("market-1")).Returns(5);

        var result = _validator.GetRemainingOrderSlots("market-1");

        Assert.Equal(0, result);
    }
}
