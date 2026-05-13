using Autotrade.OpportunityDiscovery.Application.Contract;

namespace Autotrade.OpportunityDiscovery.Application;

public sealed class OpportunityEvidenceExplainService : IOpportunityEvidenceExplainService
{
    private readonly IEvidenceSnapshotRepository _evidenceSnapshots;

    public OpportunityEvidenceExplainService(IEvidenceSnapshotRepository evidenceSnapshots)
    {
        _evidenceSnapshots = evidenceSnapshots ?? throw new ArgumentNullException(nameof(evidenceSnapshots));
    }

    public async Task<OpportunityEvidenceExplainDto?> ExplainAsync(
        Guid opportunityId,
        DateTimeOffset? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (opportunityId == Guid.Empty)
        {
            throw new ArgumentException("OpportunityId cannot be empty.", nameof(opportunityId));
        }

        var resolvedAsOf = asOfUtc ?? DateTimeOffset.UtcNow;
        var bundle = await _evidenceSnapshots
            .GetForOpportunityAsOfAsync(opportunityId, resolvedAsOf, cancellationToken)
            .ConfigureAwait(false);
        if (bundle is null)
        {
            return null;
        }

        var snapshot = SourceRegistryMapper.ToDto(bundle);
        return new OpportunityEvidenceExplainDto(
            opportunityId,
            resolvedAsOf,
            snapshot,
            snapshot.LiveGateStatus == Domain.Shared.Enums.EvidenceSnapshotLiveGateStatus.Eligible,
            snapshot.LiveGateReasons);
    }
}
