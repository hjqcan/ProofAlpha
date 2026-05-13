using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using NetDevPack.Domain;

namespace Autotrade.OpportunityDiscovery.Domain.Entities;

public sealed class OpportunityLifecycleTransition : Entity
{
    private OpportunityLifecycleTransition()
    {
        Actor = string.Empty;
        Reason = string.Empty;
        EvidenceIdsJson = "[]";
        OccurredAtUtc = DateTimeOffset.UtcNow;
    }

    public OpportunityLifecycleTransition(
        Guid hypothesisId,
        OpportunityHypothesisStatus fromStatus,
        OpportunityHypothesisStatus toStatus,
        string actor,
        string reason,
        string evidenceIdsJson,
        DateTimeOffset occurredAtUtc)
    {
        HypothesisId = hypothesisId == Guid.Empty
            ? throw new ArgumentException("HypothesisId cannot be empty.", nameof(hypothesisId))
            : hypothesisId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Actor = Required(actor, nameof(actor), 128);
        Reason = Required(reason, nameof(reason), 2048);
        EvidenceIdsJson = string.IsNullOrWhiteSpace(evidenceIdsJson) ? "[]" : evidenceIdsJson.Trim();
        OccurredAtUtc = occurredAtUtc == default ? DateTimeOffset.UtcNow : occurredAtUtc;
    }

    public Guid HypothesisId { get; private set; }

    public OpportunityHypothesisStatus FromStatus { get; private set; }

    public OpportunityHypothesisStatus ToStatus { get; private set; }

    public string Actor { get; private set; }

    public string Reason { get; private set; }

    public string EvidenceIdsJson { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

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
