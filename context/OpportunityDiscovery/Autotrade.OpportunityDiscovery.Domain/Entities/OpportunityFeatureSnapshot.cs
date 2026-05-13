using NetDevPack.Domain;

namespace Autotrade.OpportunityDiscovery.Domain.Entities;

public sealed class OpportunityFeatureSnapshot : Entity, IAggregateRoot
{
    private OpportunityFeatureSnapshot()
    {
        MarketTapeSliceId = string.Empty;
        FeatureVersion = string.Empty;
        FeaturesJson = "{}";
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public OpportunityFeatureSnapshot(
        Guid hypothesisId,
        Guid evidenceSnapshotId,
        string marketTapeSliceId,
        string featureVersion,
        string featuresJson,
        DateTimeOffset createdAtUtc)
    {
        HypothesisId = hypothesisId == Guid.Empty
            ? throw new ArgumentException("HypothesisId cannot be empty.", nameof(hypothesisId))
            : hypothesisId;
        EvidenceSnapshotId = evidenceSnapshotId == Guid.Empty
            ? throw new ArgumentException("EvidenceSnapshotId cannot be empty.", nameof(evidenceSnapshotId))
            : evidenceSnapshotId;
        MarketTapeSliceId = Required(marketTapeSliceId, nameof(marketTapeSliceId), 256);
        FeatureVersion = Required(featureVersion, nameof(featureVersion), 64);
        FeaturesJson = string.IsNullOrWhiteSpace(featuresJson) ? "{}" : featuresJson.Trim();
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc;
    }

    public Guid HypothesisId { get; private set; }

    public Guid EvidenceSnapshotId { get; private set; }

    public string MarketTapeSliceId { get; private set; }

    public string FeatureVersion { get; private set; }

    public string FeaturesJson { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

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
