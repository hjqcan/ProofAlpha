using Autotrade.Trading.Application.Execution;
using Xunit;

namespace Autotrade.Trading.Tests.Execution;

public sealed class UsdcAmountParserTests
{
    [Theory]
    [InlineData("0", 0.0)]
    [InlineData("1000000", 1.0)]
    [InlineData("1230000", 1.23)]
    [InlineData("1.23", 1.23)]
    [InlineData("  1.23  ", 1.23)]
    [InlineData("1e-6", 0.000001)]
    public void TryParse_valid_inputs(string raw, double expected)
    {
        var ok = UsdcAmountParser.TryParse(raw, out var usdc);
        Assert.True(ok);
        Assert.Equal((decimal)expected, usdc, 6);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-1")]
    [InlineData("-1000000")]
    [InlineData("not-a-number")]
    [InlineData("1,23")] // culture-dependent input should be rejected (InvariantCulture)
    public void TryParse_invalid_inputs(string? raw)
    {
        var ok = UsdcAmountParser.TryParse(raw, out _);
        Assert.False(ok);
    }
}

