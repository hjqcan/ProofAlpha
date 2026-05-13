using Autotrade.OpportunityDiscovery.Domain.Entities;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;

namespace Autotrade.OpportunityDiscovery.Application.Evidence;

public sealed record EvidenceLiveGateResult(
    EvidenceSnapshotLiveGateStatus Status,
    IReadOnlyList<string> BlockingReasons);

public static class EvidenceLiveGateEvaluator
{
    private const string ConfirmationRequiredReason =
        "Live promotion requires official API confirmation or strong multi-source confirmation; non-official news/search-only evidence is not sufficient.";

    public static EvidenceLiveGateResult Evaluate(
        IReadOnlyList<EvidenceCitation> citations,
        IReadOnlyList<EvidenceConflict> conflicts,
        IReadOnlyList<OfficialConfirmation> officialConfirmations)
    {
        ArgumentNullException.ThrowIfNull(citations);
        ArgumentNullException.ThrowIfNull(conflicts);
        ArgumentNullException.ThrowIfNull(officialConfirmations);

        var reasons = new List<string>();
        if (citations.Count == 0)
        {
            reasons.Add("Live promotion requires at least one point-in-time evidence citation.");
        }

        if (conflicts.Any(conflict =>
                conflict.BlocksLivePromotion &&
                conflict.Severity is EvidenceConflictSeverity.High or EvidenceConflictSeverity.Critical))
        {
            reasons.Add("Live promotion is blocked by unresolved high-severity source conflict.");
        }

        var hasOfficialApiConfirmation = officialConfirmations.Any(item => item.CanSatisfyLiveGate);
        var hasOfficialCitation = citations.Any(item => item.IsLiveAuthoritative);
        if (!hasOfficialApiConfirmation && !hasOfficialCitation)
        {
            reasons.Add(ConfirmationRequiredReason);
        }

        return reasons.Count == 0
            ? new EvidenceLiveGateResult(EvidenceSnapshotLiveGateStatus.Eligible, Array.Empty<string>())
            : new EvidenceLiveGateResult(EvidenceSnapshotLiveGateStatus.Blocked, reasons);
    }
}
