using Autotrade.Application.Services;

namespace Autotrade.ArcSettlement.Application.Contract.Performance;

public enum ArcPerformanceOutcomeStatus
{
    ExecutedWin = 0,
    ExecutedLoss = 1,
    ExecutedFlat = 2,
    RejectedRisk = 3,
    RejectedCompliance = 4,
    Expired = 5,
    SkippedNoAccess = 6,
    FailedExecution = 7,
    CancelledOperator = 8
}

public enum ArcPerformanceRecordStatus
{
    Pending = 0,
    SkippedDisabled = 1,
    Submitted = 2,
    Confirmed = 3,
    Failed = 4,
    Duplicate = 5
}

public enum ArcPerformanceEvidenceKind
{
    StrategyDecision = 0,
    OrderEvent = 1,
    Trade = 2,
    PaperRunReport = 3,
    RiskEvent = 4,
    ReplayExport = 5
}

public sealed record ArcPerformanceEvidenceReference(
    ArcPerformanceEvidenceKind Kind,
    string Id,
    string? Detail = null);

public sealed record BuildArcPerformanceOutcomeRequest(
    string SignalId,
    string ExecutionId,
    string StrategyId,
    string MarketId,
    DateTimeOffset ValidUntilUtc,
    ArcPerformanceOutcomeStatus Status,
    decimal? RealizedPnlBps,
    decimal? SlippageBps,
    decimal? FillRate,
    string? ReasonCode,
    IReadOnlyList<ArcPerformanceEvidenceReference> EvidenceReferences,
    DateTimeOffset? CreatedAtUtc = null);

public sealed record RecordArcPerformanceOutcomeRequest(
    string SignalId,
    string ExecutionId,
    string StrategyId,
    string MarketId,
    ArcPerformanceOutcomeStatus Status,
    decimal? RealizedPnlBps,
    decimal? SlippageBps,
    decimal? FillRate,
    string? ReasonCode,
    DateTimeOffset? CreatedAtUtc = null);

public sealed record ArcPerformanceOutcomeRecord(
    string OutcomeId,
    string SignalId,
    string ExecutionId,
    string StrategyId,
    string MarketId,
    ArcPerformanceOutcomeStatus Status,
    decimal? RealizedPnlBps,
    decimal? SlippageBps,
    decimal? FillRate,
    string? ReasonCode,
    string OutcomeHash,
    string? TransactionHash,
    string? ExplorerUrl,
    ArcPerformanceRecordStatus RecordStatus,
    string? ErrorCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset RecordedAtUtc);

public sealed record ArcPerformanceRecordResult(
    ArcPerformanceOutcomeRecord Record,
    bool AlreadyRecorded);

public sealed record ArcAgentReputation(
    string Scope,
    string? StrategyId,
    int TotalSignals,
    int TerminalSignals,
    int PendingSignals,
    int ExecutedSignals,
    int ExpiredSignals,
    int RejectedSignals,
    int SkippedSignals,
    int FailedSignals,
    int CancelledSignals,
    int WinCount,
    int LossCount,
    int FlatCount,
    decimal? AverageRealizedPnlBps,
    decimal? AverageSlippageBps,
    decimal RiskRejectionRate,
    decimal ConfidenceCoverage,
    DateTimeOffset CalculatedAtUtc);

public interface IArcPerformanceOutcomeBuilder : IApplicationService
{
    RecordArcPerformanceOutcomeRequest Build(BuildArcPerformanceOutcomeRequest request);
}

public interface IArcPerformanceRecorder : IApplicationService
{
    Task<ArcPerformanceRecordResult> RecordAsync(
        RecordArcPerformanceOutcomeRequest request,
        CancellationToken cancellationToken = default);

    Task<ArcAgentReputation> GetAgentReputationAsync(
        CancellationToken cancellationToken = default);

    Task<ArcAgentReputation> GetStrategyReputationAsync(
        string strategyId,
        CancellationToken cancellationToken = default);

    Task<ArcPerformanceOutcomeRecord?> GetOutcomeAsync(
        string signalId,
        CancellationToken cancellationToken = default);
}
