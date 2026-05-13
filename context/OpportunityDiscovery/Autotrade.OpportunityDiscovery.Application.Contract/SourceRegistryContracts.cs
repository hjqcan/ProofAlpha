using Autotrade.Application.Services;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;

namespace Autotrade.OpportunityDiscovery.Application.Contract;

public sealed record SourceProfileDto(
    Guid Id,
    string SourceKey,
    EvidenceSourceKind SourceKind,
    string SourceName,
    SourceAuthorityKind AuthorityKind,
    bool IsOfficial,
    int ExpectedLatencySeconds,
    string CoveredCategoriesJson,
    decimal HistoricalConflictRate,
    decimal HistoricalPassedGateContribution,
    decimal ReliabilityScore,
    int Version,
    Guid? SupersedesProfileId,
    string ChangeReason,
    DateTimeOffset CreatedAtUtc,
    bool CanProvideLiveConfirmation);

public sealed record AppendSourceProfileVersionRequest(
    string SourceKey,
    EvidenceSourceKind SourceKind,
    string SourceName,
    SourceAuthorityKind AuthorityKind,
    bool IsOfficial,
    int ExpectedLatencySeconds,
    IReadOnlyList<string> CoveredCategories,
    decimal HistoricalConflictRate,
    decimal HistoricalPassedGateContribution,
    decimal ReliabilityScore,
    string ChangeReason);

public sealed record SourceObservationDto(
    Guid Id,
    string SourceKey,
    SourceObservationKind ObservationKind,
    Guid? SourceProfileId,
    Guid? EvidenceSnapshotId,
    Guid? OpportunityId,
    DateTimeOffset ObservedAtUtc,
    decimal Confidence,
    string ObservationJson,
    DateTimeOffset CreatedAtUtc);

public sealed record EvidenceSnapshotDto(
    Guid Id,
    Guid OpportunityId,
    Guid ResearchRunId,
    string MarketId,
    DateTimeOffset SnapshotAsOfUtc,
    EvidenceSnapshotLiveGateStatus LiveGateStatus,
    IReadOnlyList<string> LiveGateReasons,
    string SummaryJson,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<EvidenceCitationDto> Citations,
    IReadOnlyList<EvidenceConflictDto> Conflicts,
    IReadOnlyList<OfficialConfirmationDto> OfficialConfirmations);

public sealed record EvidenceCitationDto(
    Guid Id,
    Guid EvidenceSnapshotId,
    Guid? EvidenceItemId,
    string SourceKey,
    EvidenceSourceKind SourceKind,
    string SourceName,
    bool IsOfficial,
    SourceAuthorityKind AuthorityKind,
    string Url,
    string Title,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset ObservedAtUtc,
    string ContentHash,
    decimal RelevanceScore,
    string ClaimJson,
    DateTimeOffset CreatedAtUtc,
    bool IsLiveAuthoritative);

public sealed record EvidenceConflictDto(
    Guid Id,
    Guid EvidenceSnapshotId,
    string ConflictKey,
    EvidenceConflictSeverity Severity,
    string Description,
    string SourceKeysJson,
    bool BlocksLivePromotion,
    DateTimeOffset DetectedAtUtc);

public sealed record OfficialConfirmationDto(
    Guid Id,
    Guid EvidenceSnapshotId,
    string SourceKey,
    EvidenceConfirmationKind ConfirmationKind,
    string Claim,
    string Url,
    decimal Confidence,
    DateTimeOffset ConfirmedAtUtc,
    string RawJson,
    bool CanSatisfyLiveGate);

public sealed record OpportunityEvidenceExplainDto(
    Guid OpportunityId,
    DateTimeOffset AsOfUtc,
    EvidenceSnapshotDto Snapshot,
    bool CanPassLivePromotion,
    IReadOnlyList<string> BlockingReasons);

public interface ISourceRegistryService : IApplicationService
{
    Task<IReadOnlyList<SourceProfileDto>> EnsureDefaultSourceProfilesAsync(
        CancellationToken cancellationToken = default);

    Task<SourceProfileDto> AppendSourceProfileVersionAsync(
        AppendSourceProfileVersionRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceProfileDto>> ListCurrentProfilesAsync(
        CancellationToken cancellationToken = default);
}

public interface IOpportunityEvidenceExplainService : IApplicationService
{
    Task<OpportunityEvidenceExplainDto?> ExplainAsync(
        Guid opportunityId,
        DateTimeOffset? asOfUtc = null,
        CancellationToken cancellationToken = default);
}
