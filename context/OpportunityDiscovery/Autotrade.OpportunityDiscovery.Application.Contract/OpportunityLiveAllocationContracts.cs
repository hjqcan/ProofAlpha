using Autotrade.Application.Services;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;

namespace Autotrade.OpportunityDiscovery.Application.Contract;

public sealed class OpportunityLiveAllocationOptions
{
    public const string SectionName = "OpportunityDiscovery:LiveAllocation";

    public const decimal DefaultSingleOrderNotionalUsdc = 5m;
    public const decimal DefaultSingleOpportunityNotionalUsdc = 10m;
    public const decimal DefaultSingleCycleNotionalUsdc = 20m;
    public const decimal DefaultGlobalActiveLiveExposureUsdc = 100m;
    public const int DefaultMaxActiveLiveOpportunities = 3;

    public decimal SingleOrderMaxNotionalUsdc { get; set; } = DefaultSingleOrderNotionalUsdc;
    public decimal SingleOpportunityMaxNotionalUsdc { get; set; } = DefaultSingleOpportunityNotionalUsdc;
    public decimal SingleCycleMaxNotionalUsdc { get; set; } = DefaultSingleCycleNotionalUsdc;
    public decimal GlobalActiveLiveExposureUsdc { get; set; } = DefaultGlobalActiveLiveExposureUsdc;
    public int MaxActiveLiveOpportunities { get; set; } = DefaultMaxActiveLiveOpportunities;
    public int MaxAccountSyncAgeSeconds { get; set; } = 300;

    public decimal MinRealizedEdge { get; set; } = 0m;
    public decimal MaxAdverseSlippageToPredictedEdgeRatio { get; set; } = 0.25m;
    public decimal MinFillRate { get; set; } = 0.20m;
    public decimal MaxDrawdownUsdc { get; set; } = 5m;
    public decimal MaxSourceDriftScore { get; set; } = 0.10m;
    public decimal MaxCalibrationDriftScore { get; set; } = 0.10m;
    public int MaxOrderBookAgeSeconds { get; set; } = 10;
}

public sealed record OpportunityLiveAllocationDto(
    Guid Id,
    Guid HypothesisId,
    Guid ExecutablePolicyId,
    decimal MaxNotional,
    decimal MaxContracts,
    DateTimeOffset ValidUntilUtc,
    OpportunityLiveAllocationStatus Status,
    string Reason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record OpportunityLiveAllocationRequest(
    Guid HypothesisId,
    Guid ExecutablePolicyId,
    decimal RequestedMaxNotional,
    decimal RequestedMaxContracts,
    DateTimeOffset ValidUntilUtc,
    string Actor,
    string Reason,
    IReadOnlyList<Guid> EvidenceIds);

public sealed record OpportunityLiveAllocationResult(
    bool Accepted,
    OpportunityLiveAllocationDto? Allocation,
    OpportunityHypothesisDto? Hypothesis,
    OpportunityLifecycleTransitionDto? Transition,
    IReadOnlyList<string> BlockingReasons,
    string MetricsJson);

public sealed record OpportunityKillCriteriaMetrics(
    decimal RealizedEdge,
    decimal PredictedEdge,
    decimal AdverseSlippage,
    decimal FillRate,
    decimal DrawdownUsdc,
    decimal SourceDriftScore,
    decimal CalibrationDriftScore,
    int OrderBookAgeSeconds,
    int RiskEventCount,
    int ComplianceEventCount);

public sealed record OpportunitySuspensionRequest(
    Guid HypothesisId,
    Guid ExecutablePolicyId,
    Guid? LiveAllocationId,
    string? StrategyId,
    string? MarketId,
    string Actor,
    string Reason,
    OpportunityKillCriteriaMetrics Metrics,
    IReadOnlyList<Guid> EvidenceIds);

public sealed record OpportunityCanceledOrderDto(
    string ClientOrderId,
    string? ExchangeOrderId,
    string MarketId,
    string? StrategyId,
    bool Accepted,
    string Status,
    string? Message);

public sealed record OpportunitySuspensionResult(
    bool Suspended,
    OpportunityHypothesisDto? Hypothesis,
    OpportunityLiveAllocationDto? Allocation,
    OpportunityLifecycleTransitionDto? Transition,
    IReadOnlyList<string> KillReasons,
    IReadOnlyList<OpportunityCanceledOrderDto> CanceledOrders,
    string MetricsJson);

public interface IOpportunityLiveAllocationService : IApplicationService
{
    Task<OpportunityLiveAllocationResult> TryCreateMicroAllocationAsync(
        OpportunityLiveAllocationRequest request,
        CancellationToken cancellationToken = default);

    Task<OpportunitySuspensionResult> SuspendIfKillCriteriaAsync(
        OpportunitySuspensionRequest request,
        CancellationToken cancellationToken = default);
}
