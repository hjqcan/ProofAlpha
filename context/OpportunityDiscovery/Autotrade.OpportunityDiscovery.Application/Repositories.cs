using Autotrade.OpportunityDiscovery.Domain.Entities;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;

namespace Autotrade.OpportunityDiscovery.Application;

public interface IResearchRunRepository
{
    Task AddAsync(ResearchRun run, CancellationToken cancellationToken = default);

    Task UpdateAsync(ResearchRun run, CancellationToken cancellationToken = default);
}

public interface IEvidenceItemRepository
{
    Task AddRangeDedupAsync(IReadOnlyList<EvidenceItem> evidence, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvidenceItem>> GetByRunAsync(Guid runId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvidenceItem>> GetByIdsAsync(IReadOnlyList<Guid> evidenceIds, CancellationToken cancellationToken = default);
}

public sealed record EvidenceSnapshotBundle(
    EvidenceSnapshot Snapshot,
    IReadOnlyList<EvidenceCitation> Citations,
    IReadOnlyList<EvidenceConflict> Conflicts,
    IReadOnlyList<OfficialConfirmation> OfficialConfirmations);

public interface ISourceProfileRepository
{
    Task AddAsync(SourceProfile profile, CancellationToken cancellationToken = default);

    Task<SourceProfile?> GetLatestByKeyAsync(string sourceKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, SourceProfile>> GetCurrentByKeysAsync(
        IReadOnlyList<string> sourceKeys,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceProfile>> ListCurrentAsync(CancellationToken cancellationToken = default);
}

public interface ISourceObservationRepository
{
    Task AddAsync(SourceObservation observation, CancellationToken cancellationToken = default);
}

public interface IEvidenceSnapshotRepository
{
    Task AddAsync(EvidenceSnapshotBundle bundle, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IReadOnlyList<EvidenceSnapshotBundle> bundles, CancellationToken cancellationToken = default);

    Task<EvidenceSnapshotBundle?> GetForOpportunityAsOfAsync(
        Guid opportunityId,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken = default);
}

public interface IOpportunityV2Repository
{
    Task<OpportunityHypothesis?> GetHypothesisAsync(Guid hypothesisId, CancellationToken cancellationToken = default);

    Task AddHypothesisAsync(OpportunityHypothesis hypothesis, CancellationToken cancellationToken = default);

    Task UpdateHypothesisAsync(OpportunityHypothesis hypothesis, CancellationToken cancellationToken = default);

    Task AddTransitionAsync(OpportunityLifecycleTransition transition, CancellationToken cancellationToken = default);

    Task UpdateHypothesisWithTransitionAsync(
        OpportunityHypothesis hypothesis,
        OpportunityLifecycleTransition transition,
        CancellationToken cancellationToken = default);

    Task AddFeatureSnapshotAsync(OpportunityFeatureSnapshot snapshot, CancellationToken cancellationToken = default);

    Task AddScoreAsync(OpportunityScore score, CancellationToken cancellationToken = default);

    Task AddFeatureSnapshotAndScoreAsync(
        OpportunityFeatureSnapshot snapshot,
        OpportunityScore score,
        CancellationToken cancellationToken = default);

    Task AddEvaluationRunAsync(OpportunityEvaluationRun run, CancellationToken cancellationToken = default);

    Task UpdateEvaluationRunAsync(OpportunityEvaluationRun run, CancellationToken cancellationToken = default);

    Task AddPromotionGateAsync(OpportunityPromotionGate gate, CancellationToken cancellationToken = default);

    Task AddEvaluationRunAndGateAsync(
        OpportunityEvaluationRun run,
        OpportunityPromotionGate gate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OpportunityPromotionGate>> ListPromotionGatesAsync(
        Guid hypothesisId,
        CancellationToken cancellationToken = default);

    Task AddExecutablePolicyAsync(ExecutableOpportunityPolicy policy, CancellationToken cancellationToken = default);

    Task UpdateExecutablePolicyAsync(ExecutableOpportunityPolicy policy, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExecutableOpportunityPolicy>> ListExecutablePoliciesAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default);

    Task AddLiveAllocationAsync(OpportunityLiveAllocation allocation, CancellationToken cancellationToken = default);
}

public interface IMarketOpportunityRepository
{
    Task AddRangeAsync(IReadOnlyList<MarketOpportunity> opportunities, CancellationToken cancellationToken = default);

    Task<MarketOpportunity?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MarketOpportunity>> ListAsync(
        OpportunityStatus? status,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MarketOpportunity>> ListPublishedAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MarketOpportunity>> ListExpirableAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    Task UpdateAsync(MarketOpportunity opportunity, CancellationToken cancellationToken = default);
}

public interface IOpportunityReviewRepository
{
    Task AddAsync(OpportunityReview review, CancellationToken cancellationToken = default);
}
