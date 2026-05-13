using System.Text.Json;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.OpportunityDiscovery.Domain.Entities;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.OpportunityDiscovery.Application;

internal static class OpportunityV2Mapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static OpportunityHypothesisDto ToDto(OpportunityHypothesis hypothesis)
        => new(
            hypothesis.Id,
            hypothesis.ResearchRunId,
            hypothesis.MarketId,
            (OutcomeSide)hypothesis.Outcome,
            hypothesis.SourceSnapshotId,
            hypothesis.MarketTapeSliceId,
            hypothesis.PromptVersion,
            hypothesis.ModelVersion,
            hypothesis.ScoreVersion,
            hypothesis.ReplaySeed,
            hypothesis.Status,
            hypothesis.Thesis,
            hypothesis.ActivePolicyId,
            hypothesis.ActiveLiveAllocationId,
            hypothesis.CreatedAtUtc,
            hypothesis.UpdatedAtUtc);

    public static OpportunityPromotionGateDto ToDto(OpportunityPromotionGate gate)
        => new(
            gate.Id,
            gate.HypothesisId,
            gate.GateKind,
            gate.Status,
            gate.Evaluator,
            gate.Reason,
            gate.MetricsJson,
            gate.EvidenceIdsJson,
            gate.EvaluatedAtUtc);

    public static OpportunityLifecycleTransitionDto ToDto(OpportunityLifecycleTransition transition)
        => new(
            transition.Id,
            transition.HypothesisId,
            transition.FromStatus,
            transition.ToStatus,
            transition.Actor,
            transition.Reason,
            transition.EvidenceIdsJson,
            transition.OccurredAtUtc);

    public static OpportunityFeatureSnapshotDto ToDto(OpportunityFeatureSnapshot snapshot)
        => new(
            snapshot.Id,
            snapshot.HypothesisId,
            snapshot.EvidenceSnapshotId,
            snapshot.MarketTapeSliceId,
            snapshot.FeatureVersion,
            snapshot.FeaturesJson,
            snapshot.CreatedAtUtc);

    public static OpportunityScoreDto ToDto(OpportunityScore score)
        => new(
            score.Id,
            score.HypothesisId,
            score.FeatureSnapshotId,
            score.ScoreVersion,
            score.LlmFairProbability,
            score.FairProbability,
            score.Confidence,
            score.Edge,
            score.MarketImpliedProbability,
            score.ExecutableEntryPrice,
            score.FeeEstimate,
            score.SlippageBuffer,
            score.NetEdge,
            score.ExecutableCapacity,
            score.CanPromote,
            score.CalibrationBucket,
            score.ComponentsJson,
            score.CreatedAtUtc);

    public static OpportunityEvaluationRunDto ToDto(OpportunityEvaluationRun run)
        => new(
            run.Id,
            run.HypothesisId,
            run.EvaluationKind,
            run.Status,
            run.RunVersion,
            run.MarketTapeSliceId,
            run.ReplaySeed,
            run.ResultJson,
            run.ErrorMessage,
            run.StartedAtUtc,
            run.CompletedAtUtc,
            run.UpdatedAtUtc);

    public static OpportunityLiveAllocationDto ToDto(OpportunityLiveAllocation allocation)
        => new(
            allocation.Id,
            allocation.HypothesisId,
            allocation.ExecutablePolicyId,
            allocation.MaxNotional,
            allocation.MaxContracts,
            allocation.ValidUntilUtc,
            allocation.Status,
            allocation.Reason,
            allocation.CreatedAtUtc,
            allocation.UpdatedAtUtc);

    public static ExecutableOpportunityPolicyDto ToDto(ExecutableOpportunityPolicyFeedItem item)
        => new(
            item.Policy.Id,
            item.Policy.HypothesisId,
            item.ScoreId,
            item.GateRunId,
            item.AllocationId,
            item.Policy.PolicyVersion,
            item.ScoreVersion,
            item.Policy.MarketId,
            (OutcomeSide)item.Policy.Outcome,
            item.Policy.Status,
            item.Policy.FairProbability,
            item.Policy.Confidence,
            item.Policy.Edge,
            item.Policy.EntryMaxPrice,
            item.Policy.TakeProfitPrice,
            item.Policy.StopLossPrice,
            item.Policy.MaxSpread,
            item.Policy.Quantity,
            item.Policy.MaxNotional,
            item.AllocationMaxNotional,
            item.AllocationMaxContracts,
            item.Policy.ValidFromUtc,
            item.Policy.ValidUntilUtc,
            DeserializeEvidenceIds(item.Policy.EvidenceIdsJson));

    private static IReadOnlyList<Guid> DeserializeEvidenceIds(string evidenceIdsJson)
    {
        if (string.IsNullOrWhiteSpace(evidenceIdsJson))
        {
            return Array.Empty<Guid>();
        }

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<Guid>>(evidenceIdsJson, JsonOptions)
                ?? Array.Empty<Guid>();
        }
        catch (JsonException)
        {
            return Array.Empty<Guid>();
        }
    }
}
