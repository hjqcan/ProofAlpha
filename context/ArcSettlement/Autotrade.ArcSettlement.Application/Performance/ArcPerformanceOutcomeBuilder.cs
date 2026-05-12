using Autotrade.ArcSettlement.Application.Contract.Performance;

namespace Autotrade.ArcSettlement.Application.Performance;

public sealed class ArcPerformanceOutcomeBuilder(TimeProvider timeProvider) : IArcPerformanceOutcomeBuilder
{
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public RecordArcPerformanceOutcomeRequest Build(BuildArcPerformanceOutcomeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var createdAtUtc = request.CreatedAtUtc ?? _timeProvider.GetUtcNow();

        ValidateIdentifiers(request);
        ValidateValidUntil(request.ValidUntilUtc);
        ValidateEvidence(request, createdAtUtc);

        return new RecordArcPerformanceOutcomeRequest(
            request.SignalId.Trim(),
            request.ExecutionId.Trim(),
            request.StrategyId.Trim(),
            request.MarketId.Trim(),
            request.Status,
            request.RealizedPnlBps,
            request.SlippageBps,
            request.FillRate,
            request.ReasonCode?.Trim(),
            createdAtUtc);
    }

    private static void ValidateIdentifiers(BuildArcPerformanceOutcomeRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SignalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StrategyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MarketId);
    }

    private static void ValidateValidUntil(DateTimeOffset validUntilUtc)
    {
        if (validUntilUtc == default)
        {
            throw new ArgumentException("Signal validUntilUtc must be specified.", nameof(validUntilUtc));
        }
    }

    private static void ValidateEvidence(
        BuildArcPerformanceOutcomeRequest request,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request.EvidenceReferences);

        if (request.EvidenceReferences.Count == 0)
        {
            throw new ArgumentException("A performance outcome requires at least one evidence reference.", nameof(request));
        }

        var kinds = new HashSet<ArcPerformanceEvidenceKind>();
        foreach (var evidence in request.EvidenceReferences)
        {
            if (evidence is null)
            {
                throw new ArgumentException("Evidence references cannot contain null entries.", nameof(request));
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(evidence.Id);
            kinds.Add(evidence.Kind);
        }

        if (IsExecuted(request.Status)
            && !HasAnyEvidence(kinds, ArcPerformanceEvidenceKind.Trade, ArcPerformanceEvidenceKind.OrderEvent))
        {
            throw new ArgumentException(
                "Executed outcomes require trade or order-event evidence.",
                nameof(request));
        }

        if (request.Status == ArcPerformanceOutcomeStatus.RejectedRisk
            && !kinds.Contains(ArcPerformanceEvidenceKind.RiskEvent))
        {
            throw new ArgumentException("Risk rejections require risk-event evidence.", nameof(request));
        }

        if (request.Status == ArcPerformanceOutcomeStatus.Expired)
        {
            ValidateExpiredOutcome(request.ValidUntilUtc, createdAtUtc, kinds);
        }
    }

    private static void ValidateExpiredOutcome(
        DateTimeOffset validUntilUtc,
        DateTimeOffset createdAtUtc,
        IReadOnlySet<ArcPerformanceEvidenceKind> kinds)
    {
        if (createdAtUtc < validUntilUtc)
        {
            throw new InvalidOperationException("An expired outcome cannot be built before signal validUntilUtc.");
        }

        if (!HasAnyEvidence(
                kinds,
                ArcPerformanceEvidenceKind.StrategyDecision,
                ArcPerformanceEvidenceKind.PaperRunReport,
                ArcPerformanceEvidenceKind.ReplayExport))
        {
            throw new ArgumentException(
                "Expired outcomes require strategy-decision, paper-run, or replay-export evidence.",
                nameof(kinds));
        }
    }

    private static bool IsExecuted(ArcPerformanceOutcomeStatus status)
        => status is ArcPerformanceOutcomeStatus.ExecutedWin
            or ArcPerformanceOutcomeStatus.ExecutedLoss
            or ArcPerformanceOutcomeStatus.ExecutedFlat;

    private static bool HasAnyEvidence(
        IReadOnlySet<ArcPerformanceEvidenceKind> kinds,
        params ArcPerformanceEvidenceKind[] requiredKinds)
        => requiredKinds.Any(kinds.Contains);
}
