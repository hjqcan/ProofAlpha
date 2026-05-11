using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Strategy.Application.Strategies.RepricingLag;

internal static class RepricingLagSignalMath
{
    public static decimal CalculateMoveBps(decimal baselinePrice, decimal currentPrice)
    {
        if (baselinePrice <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(baselinePrice), baselinePrice,
                "Baseline price must be positive.");
        }

        return (currentPrice - baselinePrice) / baselinePrice * 10_000m;
    }

    public static bool TryGetConfirmedOutcome(
        decimal moveBps,
        decimal minMoveBps,
        out OutcomeSide outcome,
        out decimal absoluteMoveBps)
    {
        absoluteMoveBps = Math.Abs(moveBps);
        if (absoluteMoveBps < minMoveBps)
        {
            outcome = default;
            return false;
        }

        outcome = moveBps >= 0m ? OutcomeSide.Yes : OutcomeSide.No;
        return true;
    }

    public static decimal CalculateFairProbability(decimal absoluteMoveBps, RepricingLagArbitrageOptions options)
    {
        var excessMove = Math.Max(0m, absoluteMoveBps - options.MinMoveBps);
        return Math.Min(
            options.MaxFairProbability,
            options.BaseFairProbability + excessMove * options.FairProbabilityPerMoveBps);
    }
}
