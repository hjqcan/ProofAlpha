using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using NetDevPack.Domain;

namespace Autotrade.OpportunityDiscovery.Domain.Entities;

public sealed class OfficialConfirmation : Entity
{
    private OfficialConfirmation()
    {
        SourceKey = string.Empty;
        Claim = string.Empty;
        Url = string.Empty;
        RawJson = "{}";
        ConfirmedAtUtc = DateTimeOffset.UtcNow;
    }

    public OfficialConfirmation(
        Guid evidenceSnapshotId,
        string sourceKey,
        EvidenceConfirmationKind confirmationKind,
        string claim,
        string url,
        decimal confidence,
        DateTimeOffset confirmedAtUtc,
        string rawJson)
    {
        EvidenceSnapshotId = evidenceSnapshotId == Guid.Empty
            ? throw new ArgumentException("EvidenceSnapshotId cannot be empty.", nameof(evidenceSnapshotId))
            : evidenceSnapshotId;
        SourceKey = Required(sourceKey, nameof(sourceKey), 128);
        ConfirmationKind = confirmationKind;
        Claim = Required(claim, nameof(claim), 2048);
        Url = Required(url, nameof(url), 2048);
        Confidence = confidence < 0m || confidence > 1m
            ? throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be in 0..1.")
            : confidence;
        ConfirmedAtUtc = confirmedAtUtc == default ? DateTimeOffset.UtcNow : confirmedAtUtc;
        RawJson = string.IsNullOrWhiteSpace(rawJson) ? "{}" : rawJson.Trim();
    }

    public Guid EvidenceSnapshotId { get; private set; }

    public string SourceKey { get; private set; }

    public EvidenceConfirmationKind ConfirmationKind { get; private set; }

    public string Claim { get; private set; }

    public string Url { get; private set; }

    public decimal Confidence { get; private set; }

    public DateTimeOffset ConfirmedAtUtc { get; private set; }

    public string RawJson { get; private set; }

    public bool CanSatisfyLiveGate =>
        Confidence >= 0.50m &&
        ConfirmationKind is EvidenceConfirmationKind.OfficialApi
            or EvidenceConfirmationKind.StrongMultiSource
            or EvidenceConfirmationKind.ManualOfficialReview;

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
