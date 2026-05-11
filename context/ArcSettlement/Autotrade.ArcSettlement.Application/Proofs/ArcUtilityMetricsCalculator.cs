using Autotrade.ArcSettlement.Application.Contract.Proofs;

namespace Autotrade.ArcSettlement.Application.Proofs;

public interface IArcUtilityMetricsCalculator
{
    ArcUtilityMetricsDocument Calculate(
        string documentVersion,
        string agentId,
        string strategyId,
        IReadOnlyList<ArcStrategySignalProofDocument> signals,
        IReadOnlyList<ArcStrategyOutcomeProofDocument> outcomes,
        DateTimeOffset calculatedAtUtc);
}

public sealed class ArcUtilityMetricsCalculator : IArcUtilityMetricsCalculator
{
    public ArcUtilityMetricsDocument Calculate(
        string documentVersion,
        string agentId,
        string strategyId,
        IReadOnlyList<ArcStrategySignalProofDocument> signals,
        IReadOnlyList<ArcStrategyOutcomeProofDocument> outcomes,
        DateTimeOffset calculatedAtUtc)
    {
        var matchingSignals = signals
            .Where(signal => IsSame(signal.StrategyId, strategyId))
            .ToArray();
        var matchingOutcomes = outcomes
            .Where(outcome => matchingSignals.Any(signal => IsSame(signal.SourceId, outcome.SignalId)))
            .ToArray();
        var executedOutcomes = matchingOutcomes
            .Where(outcome => outcome.Status == ArcSignalOutcomeStatus.Executed)
            .ToArray();
        var paperExecutedOutcomes = executedOutcomes
            .Where(outcome => outcome.PaperOrLive is ArcProofExecutionMode.Paper or ArcProofExecutionMode.Replay or ArcProofExecutionMode.Fixture)
            .ToArray();
        var outcomeSignalIds = matchingOutcomes
            .Select(outcome => outcome.SignalId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var signalsWithoutTerminalOutcome = matchingSignals
            .Where(signal => !outcomeSignalIds.Contains(signal.SourceId))
            .ToArray();
        var derivedExpiredSignalsCount = signalsWithoutTerminalOutcome
            .Count(signal => signal.ValidUntilUtc <= calculatedAtUtc);
        var pendingSignalsCount = signalsWithoutTerminalOutcome.Length - derivedExpiredSignalsCount;
        var terminalOutcomeCount = matchingOutcomes.Length + derivedExpiredSignalsCount;

        return new ArcUtilityMetricsDocument(
            documentVersion,
            agentId,
            strategyId,
            matchingSignals.Length,
            executedOutcomes.Length,
            Count(matchingOutcomes, ArcSignalOutcomeStatus.Expired) + derivedExpiredSignalsCount,
            Count(matchingOutcomes, ArcSignalOutcomeStatus.Rejected),
            Count(matchingOutcomes, ArcSignalOutcomeStatus.Skipped),
            Count(matchingOutcomes, ArcSignalOutcomeStatus.Failed),
            Count(matchingOutcomes, ArcSignalOutcomeStatus.Revoked),
            pendingSignalsCount,
            CalculateWinRate(paperExecutedOutcomes),
            Average(paperExecutedOutcomes.Select(outcome => outcome.RealizedPnlBps)),
            Average(paperExecutedOutcomes.Select(outcome => outcome.SlippageBps)),
            AverageSignalToExecutionSeconds(matchingSignals, executedOutcomes),
            CalculateMaxDrawdownBps(paperExecutedOutcomes),
            terminalOutcomeCount,
            terminalOutcomeCount,
            calculatedAtUtc);
    }

    private static int Count(IReadOnlyList<ArcStrategyOutcomeProofDocument> outcomes, ArcSignalOutcomeStatus status)
        => outcomes.Count(outcome => outcome.Status == status);

    private static decimal? CalculateWinRate(IReadOnlyList<ArcStrategyOutcomeProofDocument> outcomes)
    {
        if (outcomes.Count == 0)
        {
            return null;
        }

        var wins = outcomes.Count(outcome => outcome.RealizedPnlBps > 0m);
        return Decimal.Divide(wins, outcomes.Count);
    }

    private static decimal? Average(IEnumerable<decimal?> values)
    {
        var materialized = values
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        return materialized.Length == 0 ? null : materialized.Average();
    }

    private static decimal? AverageSignalToExecutionSeconds(
        IReadOnlyList<ArcStrategySignalProofDocument> signals,
        IReadOnlyList<ArcStrategyOutcomeProofDocument> outcomes)
    {
        var durations = outcomes
            .Select(outcome =>
            {
                var signal = signals.FirstOrDefault(item => IsSame(item.SourceId, outcome.SignalId));
                return signal is null ? (decimal?)null : Convert.ToDecimal((outcome.CompletedAtUtc - signal.CreatedAtUtc).TotalSeconds);
            })
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();

        return durations.Length == 0 ? null : durations.Average();
    }

    private static decimal? CalculateMaxDrawdownBps(IReadOnlyList<ArcStrategyOutcomeProofDocument> outcomes)
    {
        if (outcomes.Count == 0)
        {
            return null;
        }

        var cumulative = 0m;
        var peak = 0m;
        var maxDrawdown = 0m;

        foreach (var outcome in outcomes.OrderBy(item => item.CompletedAtUtc))
        {
            cumulative += outcome.RealizedPnlBps ?? 0m;
            peak = Math.Max(peak, cumulative);
            maxDrawdown = Math.Max(maxDrawdown, peak - cumulative);
        }

        return maxDrawdown;
    }

    private static bool IsSame(string left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
