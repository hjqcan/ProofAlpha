using Autotrade.Application.Services;

namespace Autotrade.ArcSettlement.Application.Contract.Revenue;

public enum ArcRevenueSourceKind
{
    SubscriptionFee = 0,
    BuilderAttributedFlow = 1,
    StrategyMarketplaceFee = 2,
    ManualDemoSettlement = 3
}

public enum ArcRevenueRecipientKind
{
    AgentOwner = 0,
    StrategyAuthor = 1,
    Platform = 2,
    Referrer = 3,
    Treasury = 4
}

public enum ArcRevenueSettlementStatus
{
    Pending = 0,
    Submitted = 1,
    Confirmed = 2,
    Failed = 3,
    SkippedDisabled = 4
}

public sealed record ArcRevenueSplitShareRequest(
    ArcRevenueRecipientKind RecipientKind,
    string WalletAddress,
    int ShareBps);

public sealed record ArcRevenueSplitAllocation(
    ArcRevenueRecipientKind RecipientKind,
    string WalletAddress,
    int ShareBps,
    long AmountMicroUsdc,
    decimal AmountUsdc);

public sealed record ArcRevenueSettlementRequest(
    string? SettlementId,
    ArcRevenueSourceKind SourceKind,
    string SignalId,
    string? ExecutionId,
    string WalletAddress,
    string StrategyId,
    decimal GrossUsdc,
    string? TokenAddress,
    IReadOnlyList<ArcRevenueSplitShareRequest>? Shares,
    string Reason,
    bool Simulated,
    string? SourceTransactionHash = null,
    DateTimeOffset? CreatedAtUtc = null);

public sealed record ArcRevenueSettlementRecord(
    string SettlementId,
    ArcRevenueSourceKind SourceKind,
    string SignalId,
    string? ExecutionId,
    string WalletAddress,
    string StrategyId,
    decimal GrossUsdc,
    long GrossMicroUsdc,
    string TokenAddress,
    IReadOnlyList<ArcRevenueSplitAllocation> Shares,
    string Reason,
    bool Simulated,
    string? SourceTransactionHash,
    string SettlementHash,
    string? TransactionHash,
    string? ExplorerUrl,
    ArcRevenueSettlementStatus Status,
    string? ErrorCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset RecordedAtUtc);

public sealed record ArcRevenueSettlementResult(
    ArcRevenueSettlementRecord Record,
    bool AlreadyRecorded);

public interface IArcRevenueSettlementRecorder : IApplicationService
{
    Task<ArcRevenueSettlementResult> RecordAsync(
        ArcRevenueSettlementRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArcRevenueSettlementRecord>> ListAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<ArcRevenueSettlementRecord?> GetAsync(
        string settlementId,
        CancellationToken cancellationToken = default);
}
