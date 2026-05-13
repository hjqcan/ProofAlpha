namespace Autotrade.MarketData.Application.Tape;

public sealed class MarketTapeGapRepairOptions
{
    public const string SectionName = "MarketData:TapeGapRepair";

    public bool Enabled { get; set; }

    public int MaxMarketsPerRun { get; set; } = 20;

    public int MaxTokensPerMarket { get; set; } = 2;

    public int MaxDepthLevels { get; set; } = 20;

    public decimal MinVolume24h { get; set; }

    public decimal MinLiquidity { get; set; }

    public void Validate()
    {
        if (MaxMarketsPerRun <= 0)
        {
            throw new InvalidOperationException("MarketData:TapeGapRepair:MaxMarketsPerRun must be positive.");
        }

        if (MaxTokensPerMarket <= 0)
        {
            throw new InvalidOperationException("MarketData:TapeGapRepair:MaxTokensPerMarket must be positive.");
        }

        if (MaxDepthLevels <= 0)
        {
            throw new InvalidOperationException("MarketData:TapeGapRepair:MaxDepthLevels must be positive.");
        }

        if (MinVolume24h < 0m)
        {
            throw new InvalidOperationException("MarketData:TapeGapRepair:MinVolume24h cannot be negative.");
        }

        if (MinLiquidity < 0m)
        {
            throw new InvalidOperationException("MarketData:TapeGapRepair:MinLiquidity cannot be negative.");
        }
    }
}
