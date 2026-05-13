using Autotrade.Application.Services;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.OpportunityDiscovery.Application.Contract;

public sealed record OpportunityScanRequest(
    string Trigger = "manual",
    decimal MinVolume24h = 500m,
    decimal MinLiquidity = 500m,
    int MaxMarkets = 20);

public sealed record OpportunityScanResult(
    ResearchRunDto Run,
    IReadOnlyList<MarketOpportunityDto> Opportunities);

public sealed record OpportunityUserMessageIngestionRequest(
    string SourceName,
    string Title,
    string Message,
    string? Url = null,
    DateTimeOffset? PublishedAtUtc = null,
    string? Actor = null,
    decimal SourceQuality = 0.55m);

public sealed record OpportunityUserMessageIngestionResult(
    ResearchRunDto Run,
    EvidenceItemDto Evidence);

public sealed record OpportunityAccountActivityEntry(
    string MarketId,
    OutcomeSide Outcome,
    OrderSide Side,
    decimal Price,
    decimal Quantity,
    DateTimeOffset ExecutedAtUtc,
    string? TransactionHash = null,
    string? Notes = null);

public sealed record OpportunityAccountActivityIngestionRequest(
    string WalletAddress,
    IReadOnlyList<OpportunityAccountActivityEntry> Activities,
    string SourceName = "public-account-activity",
    string? Actor = null,
    DateTimeOffset? ObservedAtUtc = null,
    string? Url = null,
    decimal SourceQuality = 0.65m);

public sealed record OpportunityAccountActivityIngestionResult(
    ResearchRunDto Run,
    EvidenceItemDto Evidence,
    string SummaryJson);

public sealed record ResearchRunDto(
    Guid Id,
    string Trigger,
    string MarketUniverseJson,
    ResearchRunStatus Status,
    int EvidenceCount,
    int OpportunityCount,
    string? ErrorMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record EvidenceItemDto(
    Guid Id,
    Guid ResearchRunId,
    EvidenceSourceKind SourceKind,
    string SourceName,
    string Url,
    string Title,
    string Summary,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset ObservedAtUtc,
    string ContentHash,
    decimal SourceQuality);

public sealed record MarketOpportunityDto(
    Guid Id,
    Guid ResearchRunId,
    string MarketId,
    OutcomeSide Outcome,
    decimal FairProbability,
    decimal Confidence,
    decimal Edge,
    OpportunityStatus Status,
    DateTimeOffset ValidUntilUtc,
    string Reason,
    string EvidenceIdsJson,
    string ScoreJson,
    string CompiledPolicyJson,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record OpportunityReviewRequest(
    Guid OpportunityId,
    string Actor,
    string? Notes = null);

public sealed record PublishedOpportunityDto(
    Guid OpportunityId,
    Guid ResearchRunId,
    string MarketId,
    OutcomeSide Outcome,
    decimal Edge,
    DateTimeOffset ValidUntilUtc,
    IReadOnlyList<Guid> EvidenceIds,
    CompiledOpportunityPolicy Policy);

public sealed record CompiledOpportunityPolicy(
    Guid OpportunityId,
    Guid ResearchRunId,
    string MarketId,
    OutcomeSide Outcome,
    decimal FairProbability,
    decimal Confidence,
    decimal Edge,
    decimal EntryMaxPrice,
    decimal TakeProfitPrice,
    decimal StopLossPrice,
    decimal MaxSpread,
    decimal Quantity,
    decimal MaxNotional,
    DateTimeOffset ValidUntilUtc,
    IReadOnlyList<Guid> EvidenceIds);

public interface IOpportunityDiscoveryService : IApplicationService
{
    Task<OpportunityScanResult> ScanAsync(
        OpportunityScanRequest request,
        CancellationToken cancellationToken = default);

    Task<OpportunityUserMessageIngestionResult> IngestUserMessageAsync(
        OpportunityUserMessageIngestionRequest request,
        CancellationToken cancellationToken = default);

    Task<OpportunityAccountActivityIngestionResult> IngestPolymarketAccountActivityAsync(
        OpportunityAccountActivityIngestionRequest request,
        CancellationToken cancellationToken = default);

    Task<MarketOpportunityDto> ApproveAsync(
        OpportunityReviewRequest request,
        CancellationToken cancellationToken = default);

    Task<MarketOpportunityDto> RejectAsync(
        OpportunityReviewRequest request,
        CancellationToken cancellationToken = default);

    Task<MarketOpportunityDto> PublishAsync(
        OpportunityReviewRequest request,
        CancellationToken cancellationToken = default);

    Task<int> ExpireStaleAsync(CancellationToken cancellationToken = default);
}

public interface IOpportunityQueryService : IApplicationService
{
    Task<IReadOnlyList<MarketOpportunityDto>> ListOpportunitiesAsync(
        OpportunityStatus? status,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<MarketOpportunityDto?> GetOpportunityAsync(
        Guid opportunityId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvidenceItemDto>> GetEvidenceAsync(
        Guid opportunityId,
        CancellationToken cancellationToken = default);
}

public interface IPublishedOpportunityFeed : IApplicationService
{
    Task<IReadOnlyList<PublishedOpportunityDto>> GetPublishedAsync(
        CancellationToken cancellationToken = default);
}
