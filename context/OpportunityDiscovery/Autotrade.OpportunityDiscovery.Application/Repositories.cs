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
