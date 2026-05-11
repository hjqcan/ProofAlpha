namespace Autotrade.MarketData.Application.Spot;

public sealed class SpotPriceStoreOptions
{
    public const string SectionName = "MarketData:SpotPriceStore";

    public int MaxTicksPerSymbol { get; set; } = 4096;

    public int MaxHistoryMinutes { get; set; } = 120;

    public void Validate()
    {
        if (MaxTicksPerSymbol <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTicksPerSymbol), MaxTicksPerSymbol,
                "MaxTicksPerSymbol must be positive.");
        }

        if (MaxHistoryMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxHistoryMinutes), MaxHistoryMinutes,
                "MaxHistoryMinutes must be positive.");
        }
    }
}
