using Autotrade.Application.Services;
using Autotrade.MarketData.Application.Contract.Tape;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;

namespace Autotrade.OpportunityDiscovery.Application.Contract;

public sealed record OpportunityEvaluationRunDto(
    Guid Id,
    Guid HypothesisId,
    OpportunityEvaluationKind EvaluationKind,
    OpportunityEvaluationRunStatus Status,
    string RunVersion,
    string MarketTapeSliceId,
    string ReplaySeed,
    string ResultJson,
    string? ErrorMessage,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record OpportunityValidationThresholds(
    int MinEvaluableSamples = 100,
    int MinObservationDays = 14,
    decimal MaxAdverseSlippageToPredictedEdgeRatio = 0.25m,
    decimal MaxSourceDriftScore = 0.10m,
    decimal MaxCalibrationDriftScore = 0.10m);

public sealed record OpportunityValidationSampleMetrics(
    int EvaluableSampleCount,
    int ObservationDays,
    decimal ScoredBrier,
    decimal MarketImpliedBrier,
    decimal SourceDriftScore,
    decimal CalibrationDriftScore,
    int CriticalRiskEventCount);

public sealed record OpportunityObservedGateMetrics(
    int EvaluableSampleCount,
    int ObservationDays,
    decimal NetPnl,
    decimal AdverseSlippage,
    decimal PredictedEdge,
    decimal ScoredBrier,
    decimal MarketImpliedBrier,
    decimal SourceDriftScore,
    decimal CalibrationDriftScore,
    int CriticalRiskEventCount,
    int BlockingIssueCount);

public sealed record OpportunityBacktestGateRequest(
    Guid HypothesisId,
    string RunVersion,
    string MarketTapeSliceId,
    DateTimeOffset EvidenceAsOfUtc,
    MarketReplayBacktestRequest ReplayRequest,
    OpportunityValidationSampleMetrics SampleMetrics,
    decimal PredictedEdge,
    IReadOnlyList<Guid> EvidenceIds,
    OpportunityValidationThresholds? Thresholds = null);

public sealed record OpportunityShadowGateRequest(
    Guid HypothesisId,
    string RunVersion,
    string MarketTapeSliceId,
    OpportunityObservedGateMetrics Metrics,
    IReadOnlyList<Guid> EvidenceIds,
    OpportunityValidationThresholds? Thresholds = null);

public sealed record OpportunityPaperGateRequest(
    Guid HypothesisId,
    string RunVersion,
    string MarketTapeSliceId,
    Guid PaperRunSessionId,
    OpportunityValidationSampleMetrics SampleMetrics,
    decimal PredictedEdge,
    int Limit = 1000,
    OpportunityValidationThresholds? Thresholds = null);

public sealed record OpportunityOperationalGateRequest(
    Guid HypothesisId,
    string RunVersion,
    string MarketTapeSliceId,
    OpportunityPromotionGateKind GateKind,
    OpportunityObservedGateMetrics Metrics,
    string Explanation,
    IReadOnlyList<Guid> EvidenceIds,
    OpportunityValidationThresholds? Thresholds = null);

public sealed record OpportunityLiveEligibilityRequest(
    Guid HypothesisId,
    Guid ActivePolicyId,
    string Actor,
    string Reason,
    IReadOnlyList<Guid> EvidenceIds);

public sealed record OpportunityGateEvaluationResult(
    OpportunityEvaluationRunDto EvaluationRun,
    OpportunityPromotionGateDto Gate,
    bool Passed,
    IReadOnlyList<string> BlockingReasons,
    string MetricsJson);

public sealed record OpportunityLiveEligibilityResult(
    bool LiveEligible,
    OpportunityHypothesisDto? Hypothesis,
    OpportunityLifecycleTransitionDto? Transition,
    IReadOnlyList<OpportunityPromotionGateDto> Gates,
    IReadOnlyList<string> BlockingReasons);

public sealed record OpportunityLifecycleTransitionDto(
    Guid Id,
    Guid HypothesisId,
    OpportunityHypothesisStatus FromStatus,
    OpportunityHypothesisStatus ToStatus,
    string Actor,
    string Reason,
    string EvidenceIdsJson,
    DateTimeOffset OccurredAtUtc);

public sealed record OpportunityPaperCriterionSnapshot(
    string Id,
    string Name,
    string Status,
    string Reason,
    IReadOnlyList<Guid> EvidenceIds,
    IReadOnlyList<string> ResidualRisks);

public sealed record OpportunityPaperValidationSnapshot(
    Guid SessionId,
    DateTimeOffset GeneratedAtUtc,
    string OverallStatus,
    bool CanConsiderLive,
    bool LiveArmingUnchanged,
    int DecisionCount,
    int TradeCount,
    int RiskEventCount,
    int CriticalRiskEventCount,
    int ObservationDays,
    decimal NetPnl,
    decimal AdverseSlippage,
    IReadOnlyList<Guid> EvidenceIds,
    IReadOnlyList<OpportunityPaperCriterionSnapshot> Criteria,
    IReadOnlyList<string> ResidualRisks);

public interface IOpportunityPaperValidationSource : IApplicationService
{
    Task<OpportunityPaperValidationSnapshot?> GetAsync(
        Guid sessionId,
        int limit = 1000,
        CancellationToken cancellationToken = default);
}

public interface IOpportunityValidationGateService : IApplicationService
{
    Task<OpportunityGateEvaluationResult> EvaluateBacktestAsync(
        OpportunityBacktestGateRequest request,
        CancellationToken cancellationToken = default);

    Task<OpportunityGateEvaluationResult> EvaluateShadowAsync(
        OpportunityShadowGateRequest request,
        CancellationToken cancellationToken = default);

    Task<OpportunityGateEvaluationResult> EvaluatePaperAsync(
        OpportunityPaperGateRequest request,
        CancellationToken cancellationToken = default);

    Task<OpportunityGateEvaluationResult> EvaluateOperationalGateAsync(
        OpportunityOperationalGateRequest request,
        CancellationToken cancellationToken = default);

    Task<OpportunityLiveEligibilityResult> TryMarkLiveEligibleAsync(
        OpportunityLiveEligibilityRequest request,
        CancellationToken cancellationToken = default);
}
