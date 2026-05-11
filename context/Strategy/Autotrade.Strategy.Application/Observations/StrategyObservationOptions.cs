namespace Autotrade.Strategy.Application.Observations;

public sealed class StrategyObservationOptions
{
    public const string SectionName = "StrategyObservations";

    public bool Enabled { get; set; } = true;

    public bool AggregateSkips { get; set; } = true;

    public int SkipAggregationWindowSeconds { get; set; } = 60;

    public int SkipSampleEvery { get; set; } = 100;

    public void Validate()
    {
        if (SkipAggregationWindowSeconds <= 0)
        {
            throw new InvalidOperationException("StrategyObservations:SkipAggregationWindowSeconds must be positive.");
        }

        if (SkipSampleEvery < 0)
        {
            throw new InvalidOperationException("StrategyObservations:SkipSampleEvery cannot be negative.");
        }
    }
}
