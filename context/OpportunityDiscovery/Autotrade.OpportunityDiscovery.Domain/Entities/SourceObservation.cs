using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using NetDevPack.Domain;

namespace Autotrade.OpportunityDiscovery.Domain.Entities;

public sealed class SourceObservation : Entity, IAggregateRoot
{
    private SourceObservation()
    {
        SourceKey = string.Empty;
        ObservationKind = SourceObservationKind.EvidenceIngested;
        ObservationJson = "{}";
        ObservedAtUtc = DateTimeOffset.UtcNow;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public SourceObservation(
        string sourceKey,
        SourceObservationKind observationKind,
        Guid? sourceProfileId,
        Guid? evidenceSnapshotId,
        Guid? opportunityId,
        DateTimeOffset observedAtUtc,
        decimal confidence,
        string observationJson,
        DateTimeOffset createdAtUtc)
    {
        SourceKey = Required(sourceKey, nameof(sourceKey), 128);
        ObservationKind = observationKind;
        SourceProfileId = sourceProfileId;
        EvidenceSnapshotId = evidenceSnapshotId;
        OpportunityId = opportunityId;
        ObservedAtUtc = observedAtUtc == default ? DateTimeOffset.UtcNow : observedAtUtc;
        Confidence = confidence < 0m || confidence > 1m
            ? throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be in 0..1.")
            : confidence;
        ObservationJson = string.IsNullOrWhiteSpace(observationJson) ? "{}" : observationJson.Trim();
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc;
    }

    public string SourceKey { get; private set; }

    public SourceObservationKind ObservationKind { get; private set; }

    public Guid? SourceProfileId { get; private set; }

    public Guid? EvidenceSnapshotId { get; private set; }

    public Guid? OpportunityId { get; private set; }

    public DateTimeOffset ObservedAtUtc { get; private set; }

    public decimal Confidence { get; private set; }

    public string ObservationJson { get; private set; }

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
