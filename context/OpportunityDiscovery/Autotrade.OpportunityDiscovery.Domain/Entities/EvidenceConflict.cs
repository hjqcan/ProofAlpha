using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using NetDevPack.Domain;

namespace Autotrade.OpportunityDiscovery.Domain.Entities;

public sealed class EvidenceConflict : Entity
{
    private EvidenceConflict()
    {
        ConflictKey = string.Empty;
        Description = string.Empty;
        SourceKeysJson = "[]";
        DetectedAtUtc = DateTimeOffset.UtcNow;
    }

    public EvidenceConflict(
        Guid evidenceSnapshotId,
        string conflictKey,
        EvidenceConflictSeverity severity,
        string description,
        string sourceKeysJson,
        bool blocksLivePromotion,
        DateTimeOffset detectedAtUtc)
    {
        EvidenceSnapshotId = evidenceSnapshotId == Guid.Empty
            ? throw new ArgumentException("EvidenceSnapshotId cannot be empty.", nameof(evidenceSnapshotId))
            : evidenceSnapshotId;
        ConflictKey = Required(conflictKey, nameof(conflictKey), 128);
        Severity = severity;
        Description = Required(description, nameof(description), 2048);
        SourceKeysJson = string.IsNullOrWhiteSpace(sourceKeysJson) ? "[]" : sourceKeysJson.Trim();
        BlocksLivePromotion = blocksLivePromotion;
        DetectedAtUtc = detectedAtUtc == default ? DateTimeOffset.UtcNow : detectedAtUtc;
    }

    public Guid EvidenceSnapshotId { get; private set; }

    public string ConflictKey { get; private set; }

    public EvidenceConflictSeverity Severity { get; private set; }

    public string Description { get; private set; }

    public string SourceKeysJson { get; private set; }

    public bool BlocksLivePromotion { get; private set; }

    public DateTimeOffset DetectedAtUtc { get; private set; }

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
