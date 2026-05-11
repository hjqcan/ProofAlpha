using Autotrade.MarketData.Application.Contract.Windows;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Strategy.Application.Strategies.RepricingLag;

public sealed record RepricingLagReplayFrame(
    DateTimeOffset TimestampUtc,
    decimal SpotPrice,
    decimal YesAsk,
    decimal NoAsk,
    decimal YesBid,
    decimal NoBid,
    decimal TopSize = 1m);

public sealed record RepricingLagReplaySignal(
    DateTimeOffset TimestampUtc,
    OutcomeSide Outcome,
    decimal SpotPrice,
    decimal MoveBps,
    decimal FairProbability,
    decimal PolymarketAsk,
    decimal Edge);

public sealed record RepricingLagReplaySummary(
    string MarketId,
    int FrameCount,
    int DetectedSignals,
    decimal AverageEdge,
    decimal RealizedWinRate,
    IReadOnlyList<RepricingLagReplaySignal> Signals);

public sealed class RepricingLagReplayRunner
{
    public RepricingLagReplaySummary Run(
        MarketWindowSpec spec,
        IReadOnlyList<RepricingLagReplayFrame> frames,
        RepricingLagArbitrageOptions options)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        if (frames.Count == 0)
        {
            return new RepricingLagReplaySummary(spec.MarketId, 0, 0, 0m, 0m, Array.Empty<RepricingLagReplaySignal>());
        }

        var ordered = frames
            .OrderBy(frame => frame.TimestampUtc)
            .ToArray();
        var baseline = ordered
            .Where(frame => frame.TimestampUtc.ToUniversalTime() <= spec.WindowStartUtc)
            .LastOrDefault();
        if (baseline == default)
        {
            baseline = ordered[0];
        }

        var confirmReadyUtc = spec.WindowStartUtc.AddSeconds(options.ConfirmWaitDurationSeconds);
        var signals = new List<RepricingLagReplaySignal>();

        foreach (var frame in ordered)
        {
            var timestampUtc = frame.TimestampUtc.ToUniversalTime();
            if (timestampUtc < confirmReadyUtc || timestampUtc >= spec.WindowEndUtc)
            {
                continue;
            }

            var moveBps = RepricingLagSignalMath.CalculateMoveBps(baseline.SpotPrice, frame.SpotPrice);
            if (!RepricingLagSignalMath.TryGetConfirmedOutcome(
                    moveBps,
                    options.MinMoveBps,
                    out var outcome,
                    out var absoluteMoveBps))
            {
                continue;
            }

            var fair = RepricingLagSignalMath.CalculateFairProbability(absoluteMoveBps, options);
            var ask = outcome == OutcomeSide.Yes ? frame.YesAsk : frame.NoAsk;
            var edge = fair - ask;
            if (edge < options.MinEdge)
            {
                continue;
            }

            signals.Add(new RepricingLagReplaySignal(
                timestampUtc,
                outcome,
                frame.SpotPrice,
                moveBps,
                fair,
                ask,
                edge));
        }

        var finalFrame = ordered[^1];
        var finalMoveBps = RepricingLagSignalMath.CalculateMoveBps(baseline.SpotPrice, finalFrame.SpotPrice);
        var wins = signals.Count(signal =>
            signal.Outcome == OutcomeSide.Yes ? finalMoveBps >= 0m : finalMoveBps < 0m);

        return new RepricingLagReplaySummary(
            spec.MarketId,
            ordered.Length,
            signals.Count,
            signals.Count == 0 ? 0m : signals.Average(signal => signal.Edge),
            signals.Count == 0 ? 0m : (decimal)wins / signals.Count,
            signals);
    }
}
