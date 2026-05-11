using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Trading.Tests.Execution;

public class ExecutionRequestValidationTests
{
    [Fact]
    public void Validate_有效请求_应返回Null()
    {
        var request = CreateValidRequest();
        var error = request.Validate();
        Assert.Null(error);
    }

    [Fact]
    public void Validate_空ClientOrderId_应返回错误()
    {
        var request = CreateValidRequest() with { ClientOrderId = "" };
        var error = request.Validate();
        Assert.NotNull(error);
        Assert.Contains("ClientOrderId", error);
    }

    [Fact]
    public void Validate_空MarketId_应返回错误()
    {
        var request = CreateValidRequest() with { MarketId = " " };
        var error = request.Validate();
        Assert.NotNull(error);
        Assert.Contains("MarketId", error);
    }

    [Fact]
    public void Validate_空TokenId_应返回错误()
    {
        var request = CreateValidRequest() with { TokenId = "" };
        var error = request.Validate();
        Assert.NotNull(error);
        Assert.Contains("TokenId", error);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.009)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void Validate_无效Price_应返回错误(decimal price)
    {
        var request = CreateValidRequest() with { Price = price };
        var error = request.Validate();
        Assert.NotNull(error);
        Assert.Contains("Price", error);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.5)]
    [InlineData(0.99)]
    public void Validate_有效Price_应通过(decimal price)
    {
        var request = CreateValidRequest() with { Price = price };
        var error = request.Validate();
        Assert.Null(error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_无效Quantity_应返回错误(decimal quantity)
    {
        var request = CreateValidRequest() with { Quantity = quantity };
        var error = request.Validate();
        Assert.NotNull(error);
        Assert.Contains("Quantity", error);
    }

    [Fact]
    public void Validate_GTD无到期时间_应返回错误()
    {
        var request = CreateValidRequest() with
        {
            TimeInForce = TimeInForce.Gtd,
            GoodTilDateUtc = null
        };
        var error = request.Validate();
        Assert.NotNull(error);
        Assert.Contains("GoodTilDateUtc", error);
    }

    [Fact]
    public void Validate_GTD到期时间已过_应返回错误()
    {
        var request = CreateValidRequest() with
        {
            TimeInForce = TimeInForce.Gtd,
            GoodTilDateUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        var error = request.Validate();
        Assert.NotNull(error);
        Assert.Contains("GoodTilDateUtc", error);
    }

    [Fact]
    public void Validate_GTD有效到期时间_应通过()
    {
        var request = CreateValidRequest() with
        {
            TimeInForce = TimeInForce.Gtd,
            GoodTilDateUtc = DateTimeOffset.UtcNow.AddHours(1)
        };
        var error = request.Validate();
        Assert.Null(error);
    }

    [Fact]
    public void Validate_Market订单_应返回错误()
    {
        var request = CreateValidRequest() with { OrderType = OrderType.Market };
        var error = request.Validate();
        Assert.NotNull(error);
        Assert.Contains("市价", error);
    }

    private static ExecutionRequest CreateValidRequest() => new()
    {
        ClientOrderId = "test-order-001",
        MarketId = "market-001",
        TokenId = "token-001",
        Outcome = OutcomeSide.Yes,
        Side = OrderSide.Buy,
        OrderType = OrderType.Limit,
        TimeInForce = TimeInForce.Gtc,
        Price = 0.5m,
        Quantity = 10m
    };
}
