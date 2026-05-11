using Autotrade.Application.Services;
using Autotrade.ArcSettlement.Application.Contract.Proofs;

namespace Autotrade.ArcSettlement.Application.Contract.Signals;

public enum ArcSignalPublicationStatus
{
    Pending = 0,
    SkippedDisabled = 1,
    Submitted = 2,
    Confirmed = 3,
    Failed = 4,
    Duplicate = 5,
    RejectedUnsafe = 6
}

public enum ArcSignalSourceReviewStatus
{
    Unknown = 0,
    Candidate = 1,
    NeedsReview = 2,
    Approved = 3,
    Published = 4,
    Rejected = 5,
    Expired = 6
}

public sealed record PublishArcSignalRequest(
    ArcStrategySignalProofDocument SignalProof,
    ArcSignalSourceReviewStatus SourceReviewStatus,
    string Actor,
    string Reason,
    string? SourcePolicyHash = null);

public sealed record ArcSignalPublicationRecord(
    string SignalId,
    ArcProofSourceKind SourceKind,
    string SourceId,
    string AgentId,
    string StrategyId,
    string MarketId,
    string Venue,
    string ReasoningHash,
    string RiskEnvelopeHash,
    decimal ExpectedEdgeBps,
    decimal MaxNotionalUsdc,
    DateTimeOffset ValidUntilUtc,
    ArcSignalPublicationStatus Status,
    string SignalHash,
    string? SourcePolicyHash,
    string? TransactionHash,
    string? ExplorerUrl,
    string? ErrorCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PublishedAtUtc,
    string Actor,
    string Reason);

public sealed record ArcSignalPublicationResult(
    ArcSignalPublicationRecord Record,
    bool AlreadyExisted);

public sealed record ArcSignalPublicationQuery(int Limit = 20);

public interface IArcSignalPublicationService : IApplicationService
{
    Task<ArcSignalPublicationResult> PublishAsync(
        PublishArcSignalRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArcSignalPublicationRecord>> ListAsync(
        ArcSignalPublicationQuery query,
        CancellationToken cancellationToken = default);

    Task<ArcSignalPublicationRecord?> GetAsync(
        string signalId,
        CancellationToken cancellationToken = default);
}
