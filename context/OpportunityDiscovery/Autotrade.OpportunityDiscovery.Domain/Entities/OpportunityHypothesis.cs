using System.Text.Json;
using Autotrade.OpportunityDiscovery.Domain.Exceptions;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using NetDevPack.Domain;

namespace Autotrade.OpportunityDiscovery.Domain.Entities;

public sealed class OpportunityHypothesis : Entity, IAggregateRoot
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static readonly IReadOnlySet<OpportunityPromotionGateKind> RequiredLiveGateKinds =
        new HashSet<OpportunityPromotionGateKind>
        {
            OpportunityPromotionGateKind.Evidence,
            OpportunityPromotionGateKind.Backtest,
            OpportunityPromotionGateKind.Shadow,
            OpportunityPromotionGateKind.Paper,
            OpportunityPromotionGateKind.ExecutionQuality,
            OpportunityPromotionGateKind.Risk,
            OpportunityPromotionGateKind.Compliance
        };

    private OpportunityHypothesis()
    {
        MarketId = string.Empty;
        Outcome = OpportunityOutcomeSide.Yes;
        Status = OpportunityHypothesisStatus.Discovered;
        Thesis = string.Empty;
        MarketTapeSliceId = string.Empty;
        PromptVersion = string.Empty;
        ModelVersion = string.Empty;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public OpportunityHypothesis(
        Guid researchRunId,
        string marketId,
        OpportunityOutcomeSide outcome,
        Guid sourceSnapshotId,
        string marketTapeSliceId,
        string promptVersion,
        string modelVersion,
        string thesis,
        DateTimeOffset discoveredAtUtc)
    {
        ResearchRunId = researchRunId == Guid.Empty
            ? throw new ArgumentException("ResearchRunId cannot be empty.", nameof(researchRunId))
            : researchRunId;
        MarketId = Required(marketId, nameof(marketId), 128);
        Outcome = outcome;
        SourceSnapshotId = sourceSnapshotId == Guid.Empty
            ? throw new ArgumentException("SourceSnapshotId cannot be empty.", nameof(sourceSnapshotId))
            : sourceSnapshotId;
        MarketTapeSliceId = Required(marketTapeSliceId, nameof(marketTapeSliceId), 256);
        PromptVersion = Required(promptVersion, nameof(promptVersion), 64);
        ModelVersion = Required(modelVersion, nameof(modelVersion), 64);
        Thesis = Required(thesis, nameof(thesis), 4096);
        Status = OpportunityHypothesisStatus.Discovered;
        CreatedAtUtc = discoveredAtUtc == default ? DateTimeOffset.UtcNow : discoveredAtUtc;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid ResearchRunId { get; private set; }

    public string MarketId { get; private set; }

    public OpportunityOutcomeSide Outcome { get; private set; }

    public Guid SourceSnapshotId { get; private set; }

    public string MarketTapeSliceId { get; private set; }

    public string PromptVersion { get; private set; }

    public string ModelVersion { get; private set; }

    public string? ScoreVersion { get; private set; }

    public string? ReplaySeed { get; private set; }

    public OpportunityHypothesisStatus Status { get; private set; }

    public string Thesis { get; private set; }

    public Guid? ActivePolicyId { get; private set; }

    public Guid? ActiveLiveAllocationId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public OpportunityLifecycleTransition MarkScored(
        string scoreVersion,
        string actor,
        string reason,
        IReadOnlyList<Guid> evidenceIds,
        DateTimeOffset now)
    {
        RequireStatus(OpportunityHypothesisStatus.Discovered, OpportunityHypothesisStatus.Scored);
        ScoreVersion = Required(scoreVersion, nameof(scoreVersion), 64);
        return TransitionTo(OpportunityHypothesisStatus.Scored, actor, reason, evidenceIds, now);
    }

    public OpportunityLifecycleTransition MarkBacktestPassed(
        string replaySeed,
        IReadOnlyList<OpportunityPromotionGate> gates,
        string actor,
        string reason,
        IReadOnlyList<Guid> evidenceIds,
        DateTimeOffset now)
    {
        RequireStatus(OpportunityHypothesisStatus.Scored, OpportunityHypothesisStatus.BacktestPassed);
        RequirePassedGate(gates, OpportunityPromotionGateKind.Backtest);
        ReplaySeed = Required(replaySeed, nameof(replaySeed), 128);
        return TransitionTo(OpportunityHypothesisStatus.BacktestPassed, actor, reason, evidenceIds, now);
    }

    public OpportunityLifecycleTransition MarkPaperValidated(
        IReadOnlyList<OpportunityPromotionGate> gates,
        string actor,
        string reason,
        IReadOnlyList<Guid> evidenceIds,
        DateTimeOffset now)
    {
        RequireStatus(OpportunityHypothesisStatus.BacktestPassed, OpportunityHypothesisStatus.PaperValidated);
        RequirePassedGate(gates, OpportunityPromotionGateKind.Paper);
        return TransitionTo(OpportunityHypothesisStatus.PaperValidated, actor, reason, evidenceIds, now);
    }

    public OpportunityLifecycleTransition MarkLiveEligible(
        IReadOnlyList<OpportunityPromotionGate> gates,
        Guid activePolicyId,
        string actor,
        string reason,
        IReadOnlyList<Guid> evidenceIds,
        DateTimeOffset now)
    {
        RequireStatus(OpportunityHypothesisStatus.PaperValidated, OpportunityHypothesisStatus.LiveEligible);
        RequirePassedLiveGates(gates);
        ActivePolicyId = activePolicyId == Guid.Empty
            ? throw new OpportunityLifecycleException(
                "OpportunityLifecycle.MissingPolicy",
                "LiveEligible requires an executable policy id.")
            : activePolicyId;
        return TransitionTo(OpportunityHypothesisStatus.LiveEligible, actor, reason, evidenceIds, now);
    }

    public OpportunityLifecycleTransition PublishLive(
        IReadOnlyList<OpportunityPromotionGate> gates,
        Guid activePolicyId,
        Guid liveAllocationId,
        string actor,
        string reason,
        IReadOnlyList<Guid> evidenceIds,
        DateTimeOffset now)
    {
        RequireStatus(OpportunityHypothesisStatus.LiveEligible, OpportunityHypothesisStatus.LivePublished);
        RequirePassedLiveGates(gates);
        if (activePolicyId == Guid.Empty || activePolicyId != ActivePolicyId)
        {
            throw new OpportunityLifecycleException(
                "OpportunityLifecycle.MissingPolicy",
                "LivePublished requires the active executable policy id.");
        }

        ActiveLiveAllocationId = liveAllocationId == Guid.Empty
            ? throw new OpportunityLifecycleException(
                "OpportunityLifecycle.MissingAllocation",
                "LivePublished requires a live allocation id.")
            : liveAllocationId;
        return TransitionTo(OpportunityHypothesisStatus.LivePublished, actor, reason, evidenceIds, now);
    }

    public OpportunityLifecycleTransition Suspend(
        string actor,
        string reason,
        IReadOnlyList<Guid> evidenceIds,
        DateTimeOffset now)
    {
        if (Status is OpportunityHypothesisStatus.Expired or OpportunityHypothesisStatus.Suspended)
        {
            throw new OpportunityLifecycleException(
                "OpportunityLifecycle.InvalidTransition",
                $"Cannot suspend hypothesis {Id} from {Status}.");
        }

        return TransitionTo(OpportunityHypothesisStatus.Suspended, actor, reason, evidenceIds, now);
    }

    public OpportunityLifecycleTransition Expire(
        string actor,
        string reason,
        IReadOnlyList<Guid> evidenceIds,
        DateTimeOffset now)
    {
        if (Status is OpportunityHypothesisStatus.Expired)
        {
            throw new OpportunityLifecycleException(
                "OpportunityLifecycle.InvalidTransition",
                $"Cannot expire hypothesis {Id} from {Status}.");
        }

        return TransitionTo(OpportunityHypothesisStatus.Expired, actor, reason, evidenceIds, now);
    }

    private OpportunityLifecycleTransition TransitionTo(
        OpportunityHypothesisStatus toStatus,
        string actor,
        string reason,
        IReadOnlyList<Guid> evidenceIds,
        DateTimeOffset now)
    {
        var resolvedNow = now == default ? DateTimeOffset.UtcNow : now;
        var fromStatus = Status;
        Status = toStatus;
        UpdatedAtUtc = resolvedNow;
        return new OpportunityLifecycleTransition(
            Id,
            fromStatus,
            toStatus,
            actor,
            reason,
            JsonSerializer.Serialize(evidenceIds.Where(id => id != Guid.Empty).Distinct().ToList(), JsonOptions),
            resolvedNow);
    }

    private void RequireStatus(
        OpportunityHypothesisStatus requiredStatus,
        OpportunityHypothesisStatus targetStatus)
    {
        if (Status != requiredStatus)
        {
            throw new OpportunityLifecycleException(
                "OpportunityLifecycle.InvalidTransition",
                $"Cannot move hypothesis {Id} from {Status} to {targetStatus}; expected {requiredStatus}.");
        }
    }

    private static void RequirePassedGate(
        IReadOnlyList<OpportunityPromotionGate> gates,
        OpportunityPromotionGateKind gateKind)
    {
        if (!gates.Any(gate => gate.GateKind == gateKind && gate.Status == OpportunityPromotionGateStatus.Passed))
        {
            throw new OpportunityLifecycleException(
                "OpportunityLifecycle.MissingGate",
                $"Transition requires passed {gateKind} gate.");
        }
    }

    private static void RequirePassedLiveGates(IReadOnlyList<OpportunityPromotionGate> gates)
    {
        var passedKinds = gates
            .Where(gate => gate.Status == OpportunityPromotionGateStatus.Passed)
            .Select(gate => gate.GateKind)
            .ToHashSet();
        var missing = RequiredLiveGateKinds
            .Where(kind => !passedKinds.Contains(kind))
            .OrderBy(kind => kind)
            .ToList();
        if (missing.Count == 0)
        {
            return;
        }

        throw new OpportunityLifecycleException(
            "OpportunityLifecycle.MissingGate",
            $"Live publication requires passed gates: {string.Join(", ", missing)}.");
    }

    private static string Required(string value, string paramName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{paramName} cannot be empty.", paramName);
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
