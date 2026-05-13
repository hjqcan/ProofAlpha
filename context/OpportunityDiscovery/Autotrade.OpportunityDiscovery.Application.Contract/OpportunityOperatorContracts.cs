using Autotrade.Application.Services;
using Autotrade.MarketData.Application.Contract.Snapshots;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;

namespace Autotrade.OpportunityDiscovery.Application.Contract;

public sealed record OpportunityScoreStatusResponse(
    Guid OpportunityId,
    OpportunityHypothesisDto? Hypothesis,
    OpportunityScoreDto? LatestScore,
    IReadOnlyList<string> BlockingReasons);

public sealed record OpportunityReplayStatusResponse(
    Guid OpportunityId,
    OpportunityHypothesisDto? Hypothesis,
    IReadOnlyList<OpportunityEvaluationRunDto> EvaluationRuns,
    IReadOnlyList<OpportunityPromotionGateDto> Gates,
    string? ReplaySeed,
    string? MarketTapeSliceId,
    IReadOnlyList<string> BlockingReasons);

public sealed record OpportunityPromoteRequest(
    Guid OpportunityId,
    string Actor,
    string Reason);

public sealed record OpportunityOperatorActionRequest(
    string? Actor = null,
    string? Reason = null,
    string? StrategyId = null,
    string? MarketId = null);

public sealed record OpportunityPromoteResponse(
    Guid OpportunityId,
    bool Accepted,
    OpportunityLiveEligibilityResult? Eligibility,
    IReadOnlyList<string> BlockingReasons);

public sealed record OpportunityLiveStatusResponse(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<ExecutableOpportunityPolicyDto> ExecutablePolicies,
    IReadOnlyList<OpportunityLiveAllocationDto> ActiveAllocations,
    IReadOnlyList<OpportunityHypothesisDto> LiveHypotheses,
    IReadOnlyList<string> BlockingReasons);

public sealed record OpportunityOperatorSuspendRequest(
    Guid OpportunityId,
    string Actor,
    string Reason,
    string? StrategyId = null,
    string? MarketId = null);

public sealed record OpportunityOperatorSuspendResponse(
    Guid OpportunityId,
    bool Suspended,
    OpportunityHypothesisDto? Hypothesis,
    OpportunityLiveAllocationDto? Allocation,
    OpportunityLifecycleTransitionDto? Transition,
    IReadOnlyList<string> Reasons);

public sealed record OpportunityExplainResponse(
    Guid OpportunityId,
    DateTimeOffset GeneratedAtUtc,
    bool Redacted,
    OpportunityHypothesisDto? Hypothesis,
    OpportunityEvidenceExplainDto? Evidence,
    IReadOnlyList<SourceProfileDto> SourceProfiles,
    IReadOnlyList<EvidenceConflictDto> SourceConflicts,
    string? MarketTapeSliceId,
    UnifiedMarketDataSnapshot? CurrentMarketDataSnapshot,
    string? PromptVersion,
    string? ModelVersion,
    string? ScoreVersion,
    OpportunityFeatureSnapshotDto? FeatureSnapshot,
    OpportunityScoreDto? Score,
    string? ScoreBreakdownJson,
    decimal? ExpectedEv,
    decimal? ExecutableCapacity,
    IReadOnlyList<OpportunityPromotionGateDto> Gates,
    IReadOnlyList<OpportunityEvaluationRunDto> EvaluationRuns,
    ExecutableOpportunityPolicyDto? ExecutablePolicy,
    OpportunityLiveAllocationDto? Allocation,
    IReadOnlyList<string> RiskReasons,
    IReadOnlyList<string> ComplianceReasons,
    IReadOnlyList<string> BlockingReasons);

public interface IOpportunityOperatorService : IApplicationService
{
    Task<OpportunityScoreStatusResponse> GetScoreAsync(
        Guid opportunityId,
        CancellationToken cancellationToken = default);

    Task<OpportunityReplayStatusResponse> GetReplayAsync(
        Guid opportunityId,
        CancellationToken cancellationToken = default);

    Task<OpportunityPromoteResponse> PromoteAsync(
        OpportunityPromoteRequest request,
        CancellationToken cancellationToken = default);

    Task<OpportunityLiveStatusResponse> GetLiveStatusAsync(
        CancellationToken cancellationToken = default);

    Task<OpportunityOperatorSuspendResponse> SuspendAsync(
        OpportunityOperatorSuspendRequest request,
        CancellationToken cancellationToken = default);

    Task<OpportunityExplainResponse> ExplainAsync(
        Guid opportunityId,
        DateTimeOffset? asOfUtc = null,
        CancellationToken cancellationToken = default);
}
