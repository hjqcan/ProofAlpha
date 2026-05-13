using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using NetDevPack.Domain;

namespace Autotrade.OpportunityDiscovery.Domain.Entities;

public sealed class EvidenceSnapshot : Entity, IAggregateRoot
{
    private EvidenceSnapshot()
    {
        MarketId = string.Empty;
        LiveGateStatus = EvidenceSnapshotLiveGateStatus.Blocked;
        LiveGateReasonsJson = "[]";
        SummaryJson = "{}";
        SnapshotAsOfUtc = DateTimeOffset.UtcNow;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public EvidenceSnapshot(
        Guid opportunityId,
        Guid researchRunId,
        string marketId,
        DateTimeOffset snapshotAsOfUtc,
        EvidenceSnapshotLiveGateStatus liveGateStatus,
        string liveGateReasonsJson,
        string summaryJson,
        DateTimeOffset createdAtUtc)
    {
        OpportunityId = opportunityId == Guid.Empty
            ? throw new ArgumentException("OpportunityId cannot be empty.", nameof(opportunityId))
            : opportunityId;
        ResearchRunId = researchRunId == Guid.Empty
            ? throw new ArgumentException("ResearchRunId cannot be empty.", nameof(researchRunId))
            : researchRunId;
        MarketId = Required(marketId, nameof(marketId), 128);
        SnapshotAsOfUtc = snapshotAsOfUtc == default ? DateTimeOffset.UtcNow : snapshotAsOfUtc;
        LiveGateStatus = liveGateStatus;
        LiveGateReasonsJson = string.IsNullOrWhiteSpace(liveGateReasonsJson) ? "[]" : liveGateReasonsJson.Trim();
        SummaryJson = string.IsNullOrWhiteSpace(summaryJson) ? "{}" : summaryJson.Trim();
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc;
    }

    public Guid OpportunityId { get; private set; }

    public Guid ResearchRunId { get; private set; }

    public string MarketId { get; private set; }

    public DateTimeOffset SnapshotAsOfUtc { get; private set; }

    public EvidenceSnapshotLiveGateStatus LiveGateStatus { get; private set; }

    public string LiveGateReasonsJson { get; private set; }

    public string SummaryJson { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public bool CanPassLivePromotion => LiveGateStatus == EvidenceSnapshotLiveGateStatus.Eligible;

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
