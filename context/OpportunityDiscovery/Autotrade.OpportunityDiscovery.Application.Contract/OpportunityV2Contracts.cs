using Autotrade.Application.Services;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.OpportunityDiscovery.Application.Contract;

public sealed record OpportunityHypothesisDto(
    Guid Id,
    Guid ResearchRunId,
    string MarketId,
    OutcomeSide Outcome,
    Guid SourceSnapshotId,
    string MarketTapeSliceId,
    string PromptVersion,
    string ModelVersion,
    string? ScoreVersion,
    string? ReplaySeed,
    OpportunityHypothesisStatus Status,
    string Thesis,
    Guid? ActivePolicyId,
    Guid? ActiveLiveAllocationId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record OpportunityPromotionGateDto(
    Guid Id,
    Guid HypothesisId,
    OpportunityPromotionGateKind GateKind,
    OpportunityPromotionGateStatus Status,
    string Evaluator,
    string Reason,
    string MetricsJson,
    string EvidenceIdsJson,
    DateTimeOffset EvaluatedAtUtc);

public sealed record OpportunityFeatureSnapshotDto(
    Guid Id,
    Guid HypothesisId,
    Guid EvidenceSnapshotId,
    string MarketTapeSliceId,
    string FeatureVersion,
    string FeaturesJson,
    DateTimeOffset CreatedAtUtc);

public sealed record OpportunityScoreDto(
    Guid Id,
    Guid HypothesisId,
    Guid FeatureSnapshotId,
    string ScoreVersion,
    decimal LlmFairProbability,
    decimal FairProbability,
    decimal Confidence,
    decimal Edge,
    decimal MarketImpliedProbability,
    decimal ExecutableEntryPrice,
    decimal FeeEstimate,
    decimal SlippageBuffer,
    decimal NetEdge,
    decimal ExecutableCapacity,
    bool CanPromote,
    string CalibrationBucket,
    string ComponentsJson,
    DateTimeOffset CreatedAtUtc);

public sealed record OpportunityFeatureVector(
    OpportunityType OpportunityType,
    decimal LlmFairProbability,
    decimal EvidenceConfidence,
    decimal OfficialConfirmationStrength,
    decimal LiquidityCapacity,
    decimal Spread,
    decimal MarketImpact,
    decimal TimeHalfLife,
    decimal SourceConflictPenalty,
    decimal ResolutionRisk,
    decimal ExecutionFreshness,
    decimal MarketClosureAmbiguity,
    decimal MarketImpliedProbability,
    decimal ExecutableEntryPrice,
    decimal FeeEstimate,
    decimal SlippageBuffer,
    decimal ExecutableCapacity);

public sealed record OpportunityScoringRequest(
    Guid HypothesisId,
    Guid EvidenceSnapshotId,
    string MarketTapeSliceId,
    string FeatureVersion,
    string ScoreVersion,
    OpportunityFeatureVector Features,
    DateTimeOffset CreatedAtUtc);

public sealed record OpportunityScoringResult(
    OpportunityFeatureSnapshotDto FeatureSnapshot,
    OpportunityScoreDto Score,
    bool CanPromote,
    IReadOnlyList<string> BlockingReasons,
    string BreakdownJson);

public sealed record ExecutableOpportunityPolicyDto(
    Guid PolicyId,
    Guid HypothesisId,
    Guid ScoreId,
    Guid GateRunId,
    Guid AllocationId,
    string PolicyVersion,
    string ScoreVersion,
    string MarketId,
    OutcomeSide Outcome,
    ExecutableOpportunityPolicyStatus Status,
    decimal FairProbability,
    decimal Confidence,
    decimal Edge,
    decimal EntryMaxPrice,
    decimal TakeProfitPrice,
    decimal StopLossPrice,
    decimal MaxSpread,
    decimal Quantity,
    decimal MaxNotional,
    decimal AllocationMaxNotional,
    decimal AllocationMaxContracts,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ValidUntilUtc,
    IReadOnlyList<Guid> EvidenceIds);

public interface IExecutableOpportunityPolicyFeed : IApplicationService
{
    Task<IReadOnlyList<ExecutableOpportunityPolicyDto>> GetExecutableAsync(
        int limit = 50,
        CancellationToken cancellationToken = default);
}

public interface IOpportunityScoringService : IApplicationService
{
    Task<OpportunityScoringResult> ScoreAsync(
        OpportunityScoringRequest request,
        CancellationToken cancellationToken = default);
}
