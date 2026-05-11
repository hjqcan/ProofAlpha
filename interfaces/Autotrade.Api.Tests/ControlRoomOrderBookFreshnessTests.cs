using Autotrade.Api.ControlRoom;

namespace Autotrade.Api.Tests;

public sealed class ControlRoomOrderBookFreshnessTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 3, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(4, "Fresh")]
    [InlineData(8, "Delayed")]
    [InlineData(45, "Stale")]
    public void EvaluateClassifiesFreshDelayedAndStaleStates(int ageSeconds, string expectedStatus)
    {
        var freshness = ControlRoomOrderBookFreshness.Evaluate(
            Now.AddSeconds(-ageSeconds),
            Now,
            freshSeconds: 5,
            staleSeconds: 30);

        Assert.Equal(expectedStatus, freshness.Status);
        Assert.Equal(ageSeconds, freshness.AgeSeconds);
        Assert.Equal(5, freshness.FreshSeconds);
        Assert.Equal(30, freshness.StaleSeconds);
        Assert.Contains(ageSeconds.ToString(), freshness.Message);
    }

    [Fact]
    public void EvaluateNormalizesInvalidThresholds()
    {
        var freshness = ControlRoomOrderBookFreshness.Evaluate(
            Now.AddSeconds(-2),
            Now,
            freshSeconds: -1,
            staleSeconds: -1);

        Assert.Equal(ControlRoomOrderBookFreshness.Delayed, freshness.Status);
        Assert.Equal(1, freshness.FreshSeconds);
        Assert.Equal(2, freshness.StaleSeconds);
    }
}
