using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using NetDevPack.Domain;

namespace Autotrade.OpportunityDiscovery.Domain.Entities;

public sealed class EvidenceCitation : Entity
{
    private EvidenceCitation()
    {
        SourceKey = string.Empty;
        SourceName = string.Empty;
        Url = string.Empty;
        Title = string.Empty;
        ContentHash = string.Empty;
        ClaimJson = "{}";
        CreatedAtUtc = DateTimeOffset.UtcNow;
        ObservedAtUtc = DateTimeOffset.UtcNow;
    }

    public EvidenceCitation(
        Guid evidenceSnapshotId,
        Guid? evidenceItemId,
        string sourceKey,
        EvidenceSourceKind sourceKind,
        string sourceName,
        bool isOfficial,
        SourceAuthorityKind authorityKind,
        string url,
        string title,
        DateTimeOffset? publishedAtUtc,
        DateTimeOffset observedAtUtc,
        string contentHash,
        decimal relevanceScore,
        string claimJson,
        DateTimeOffset createdAtUtc)
    {
        EvidenceSnapshotId = evidenceSnapshotId == Guid.Empty
            ? throw new ArgumentException("EvidenceSnapshotId cannot be empty.", nameof(evidenceSnapshotId))
            : evidenceSnapshotId;
        EvidenceItemId = evidenceItemId;
        SourceKey = Required(sourceKey, nameof(sourceKey), 128);
        SourceKind = sourceKind;
        SourceName = Required(sourceName, nameof(sourceName), 128);
        IsOfficial = isOfficial;
        AuthorityKind = authorityKind;
        Url = Required(url, nameof(url), 2048);
        Title = Required(title, nameof(title), 512);
        PublishedAtUtc = publishedAtUtc;
        ObservedAtUtc = observedAtUtc == default ? DateTimeOffset.UtcNow : observedAtUtc;
        ContentHash = Required(contentHash, nameof(contentHash), 128);
        RelevanceScore = relevanceScore < 0m || relevanceScore > 1m
            ? throw new ArgumentOutOfRangeException(nameof(relevanceScore), relevanceScore, "RelevanceScore must be in 0..1.")
            : relevanceScore;
        ClaimJson = string.IsNullOrWhiteSpace(claimJson) ? "{}" : claimJson.Trim();
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc;
    }

    public Guid EvidenceSnapshotId { get; private set; }

    public Guid? EvidenceItemId { get; private set; }

    public string SourceKey { get; private set; }

    public EvidenceSourceKind SourceKind { get; private set; }

    public string SourceName { get; private set; }

    public bool IsOfficial { get; private set; }

    public SourceAuthorityKind AuthorityKind { get; private set; }

    public string Url { get; private set; }

    public string Title { get; private set; }

    public DateTimeOffset? PublishedAtUtc { get; private set; }

    public DateTimeOffset ObservedAtUtc { get; private set; }

    public string ContentHash { get; private set; }

    public decimal RelevanceScore { get; private set; }

    public string ClaimJson { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public bool IsLiveAuthoritative =>
        IsOfficial ||
        AuthorityKind is SourceAuthorityKind.Official
            or SourceAuthorityKind.PrimaryExchange
            or SourceAuthorityKind.Regulator
            or SourceAuthorityKind.DataOracle;

    private static string Required(string value, string paramName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{paramName} cannot be empty.", paramName);
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
