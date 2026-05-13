using System.Text.Json;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.OpportunityDiscovery.Domain.Entities;

namespace Autotrade.OpportunityDiscovery.Application;

internal static class SourceRegistryMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static SourceProfileDto ToDto(SourceProfile profile)
        => new(
            profile.Id,
            profile.SourceKey,
            profile.SourceKind,
            profile.SourceName,
            profile.AuthorityKind,
            profile.IsOfficial,
            profile.ExpectedLatencySeconds,
            profile.CoveredCategoriesJson,
            profile.HistoricalConflictRate,
            profile.HistoricalPassedGateContribution,
            profile.ReliabilityScore,
            profile.Version,
            profile.SupersedesProfileId,
            profile.ChangeReason,
            profile.CreatedAtUtc,
            profile.CanProvideLiveConfirmation);

    public static SourceObservationDto ToDto(SourceObservation observation)
        => new(
            observation.Id,
            observation.SourceKey,
            observation.ObservationKind,
            observation.SourceProfileId,
            observation.EvidenceSnapshotId,
            observation.OpportunityId,
            observation.ObservedAtUtc,
            observation.Confidence,
            observation.ObservationJson,
            observation.CreatedAtUtc);

    public static EvidenceSnapshotDto ToDto(EvidenceSnapshotBundle bundle)
        => new(
            bundle.Snapshot.Id,
            bundle.Snapshot.OpportunityId,
            bundle.Snapshot.ResearchRunId,
            bundle.Snapshot.MarketId,
            bundle.Snapshot.SnapshotAsOfUtc,
            bundle.Snapshot.LiveGateStatus,
            DeserializeReasons(bundle.Snapshot.LiveGateReasonsJson),
            bundle.Snapshot.SummaryJson,
            bundle.Snapshot.CreatedAtUtc,
            bundle.Citations.Select(ToDto).ToList(),
            bundle.Conflicts.Select(ToDto).ToList(),
            bundle.OfficialConfirmations.Select(ToDto).ToList());

    public static EvidenceCitationDto ToDto(EvidenceCitation citation)
        => new(
            citation.Id,
            citation.EvidenceSnapshotId,
            citation.EvidenceItemId,
            citation.SourceKey,
            citation.SourceKind,
            citation.SourceName,
            citation.IsOfficial,
            citation.AuthorityKind,
            citation.Url,
            citation.Title,
            citation.PublishedAtUtc,
            citation.ObservedAtUtc,
            citation.ContentHash,
            citation.RelevanceScore,
            citation.ClaimJson,
            citation.CreatedAtUtc,
            citation.IsLiveAuthoritative);

    public static EvidenceConflictDto ToDto(EvidenceConflict conflict)
        => new(
            conflict.Id,
            conflict.EvidenceSnapshotId,
            conflict.ConflictKey,
            conflict.Severity,
            conflict.Description,
            conflict.SourceKeysJson,
            conflict.BlocksLivePromotion,
            conflict.DetectedAtUtc);

    public static OfficialConfirmationDto ToDto(OfficialConfirmation confirmation)
        => new(
            confirmation.Id,
            confirmation.EvidenceSnapshotId,
            confirmation.SourceKey,
            confirmation.ConfirmationKind,
            confirmation.Claim,
            confirmation.Url,
            confirmation.Confidence,
            confirmation.ConfirmedAtUtc,
            confirmation.RawJson,
            confirmation.CanSatisfyLiveGate);

    private static IReadOnlyList<string> DeserializeReasons(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<string>>(json, JsonOptions) ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return [json];
        }
    }
}
