using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Execution;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Trading.Tests.Execution;

public class TimeInForceHandlerTests
{
    [Theory]
    [InlineData(TimeInForce.Gtc)]
    [InlineData(TimeInForce.Fak)]
    [InlineData(TimeInForce.Fok)]
    public void Validate_非GTD订单_应通过(TimeInForce tif)
    {
        var request = CreateRequest(tif, null);
        var error = TimeInForceHandler.Validate(request);
        Assert.Null(error);
    }

    [Fact]
    public void Validate_GTD无到期时间_应返回错误()
    {
        var request = CreateRequest(TimeInForce.Gtd, null);
        var error = TimeInForceHandler.Validate(request);
        Assert.NotNull(error);
    }

    [Fact]
    public void Validate_GTD到期时间已过_应返回错误()
    {
        var request = CreateRequest(TimeInForce.Gtd, DateTimeOffset.UtcNow.AddMinutes(-1));
        var error = TimeInForceHandler.Validate(request);
        Assert.NotNull(error);
    }

    [Fact]
    public void Validate_GTD有效到期时间_应通过()
    {
        var request = CreateRequest(TimeInForce.Gtd, DateTimeOffset.UtcNow.AddHours(1));
        var error = TimeInForceHandler.Validate(request);
        Assert.Null(error);
    }

    [Fact]
    public void ShouldCancelRemaining_FOK未全部成交_应返回True()
    {
        var result = TimeInForceHandler.ShouldCancelRemaining(TimeInForce.Fok, 5m, 10m);
        Assert.True(result);
    }

    [Fact]
    public void ShouldCancelRemaining_FOK全部成交_应返回False()
    {
        var result = TimeInForceHandler.ShouldCancelRemaining(TimeInForce.Fok, 10m, 10m);
        Assert.False(result);
    }

    [Fact]
    public void ShouldCancelRemaining_FAK部分成交_应返回True()
    {
        var result = TimeInForceHandler.ShouldCancelRemaining(TimeInForce.Fak, 5m, 10m);
        Assert.True(result);
    }

    [Fact]
    public void ShouldCancelRemaining_FAK无成交_应返回True()
    {
        var result = TimeInForceHandler.ShouldCancelRemaining(TimeInForce.Fak, 0m, 10m);
        Assert.True(result);
    }

    [Fact]
    public void ShouldCancelRemaining_GTC部分成交_应返回False()
    {
        var result = TimeInForceHandler.ShouldCancelRemaining(TimeInForce.Gtc, 5m, 10m);
        Assert.False(result);
    }

    [Fact]
    public void IsExpired_GTD已过期_应返回True()
    {
        var result = TimeInForceHandler.IsExpired(TimeInForce.Gtd, DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.True(result);
    }

    [Fact]
    public void IsExpired_GTD未过期_应返回False()
    {
        var result = TimeInForceHandler.IsExpired(TimeInForce.Gtd, DateTimeOffset.UtcNow.AddHours(1));
        Assert.False(result);
    }

    [Fact]
    public void IsExpired_非GTD订单_应返回False()
    {
        var result = TimeInForceHandler.IsExpired(TimeInForce.Gtc, DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.False(result);
    }

    [Fact]
    public void GetTimeToExpiry_GTD未过期_应返回正时间段()
    {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(10);
        var result = TimeInForceHandler.GetTimeToExpiry(TimeInForce.Gtd, expiry);

        Assert.NotNull(result);
        Assert.True(result.Value > TimeSpan.Zero);
    }

    [Fact]
    public void GetTimeToExpiry_GTD已过期_应返回零()
    {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(-1);
        var result = TimeInForceHandler.GetTimeToExpiry(TimeInForce.Gtd, expiry);

        Assert.NotNull(result);
        Assert.Equal(TimeSpan.Zero, result.Value);
    }

    [Fact]
    public void GetTimeToExpiry_非GTD订单_应返回Null()
    {
        var result = TimeInForceHandler.GetTimeToExpiry(TimeInForce.Gtc, DateTimeOffset.UtcNow.AddHours(1));
        Assert.Null(result);
    }

    [Fact]
    public void GetEffectiveStatus_GTD已过期_应返回Expired()
    {
        var status = TimeInForceHandler.GetEffectiveStatus(
            TimeInForce.Gtd,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            0m,
            10m,
            ExecutionStatus.Accepted);

        Assert.Equal(ExecutionStatus.Expired, status);
    }

    [Fact]
    public void GetEffectiveStatus_FOK未全成交_应返回Cancelled()
    {
        var status = TimeInForceHandler.GetEffectiveStatus(
            TimeInForce.Fok,
            null,
            5m,
            10m,
            ExecutionStatus.Accepted);

        // FOK 未全成交应返回 Cancelled，不允许部分成交
        Assert.Equal(ExecutionStatus.Cancelled, status);
    }

    [Fact]
    public void GetEffectiveStatus_FAK部分成交_应返回Cancelled()
    {
        var status = TimeInForceHandler.GetEffectiveStatus(
            TimeInForce.Fak,
            null,
            5m,
            10m,
            ExecutionStatus.Accepted);

        Assert.Equal(ExecutionStatus.Cancelled, status);
    }

    [Fact]
    public void GetEffectiveStatus_已终态_不应改变()
    {
        var status = TimeInForceHandler.GetEffectiveStatus(
            TimeInForce.Gtd,
            DateTimeOffset.UtcNow.AddMinutes(-1), // 已过期
            10m,
            10m,
            ExecutionStatus.Filled); // 但已成交

        // 已终态不应改变
        Assert.Equal(ExecutionStatus.Filled, status);
    }

    private static ExecutionRequest CreateRequest(TimeInForce tif, DateTimeOffset? expiry) => new()
    {
        ClientOrderId = "test-order",
        MarketId = "market-001",
        TokenId = "token-001",
        Outcome = OutcomeSide.Yes,
        Side = OrderSide.Buy,
        OrderType = OrderType.Limit,
        TimeInForce = tif,
        Price = 0.5m,
        Quantity = 10m,
        GoodTilDateUtc = expiry
    };
}
