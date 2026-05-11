using Autotrade.OpportunityDiscovery.Application;
using Autotrade.OpportunityDiscovery.Domain.Entities;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using Autotrade.OpportunityDiscovery.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Autotrade.OpportunityDiscovery.Infra.Data.Repositories;

public sealed class ResearchRunRepository : IResearchRunRepository
{
    private readonly OpportunityDiscoveryContext _context;

    public ResearchRunRepository(OpportunityDiscoveryContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task AddAsync(ResearchRun run, CancellationToken cancellationToken = default)
    {
        _context.ResearchRuns.Add(run);
        await _context.Commit().ConfigureAwait(false);
    }

    public async Task UpdateAsync(ResearchRun run, CancellationToken cancellationToken = default)
    {
        _context.ResearchRuns.Update(run);
        await _context.Commit().ConfigureAwait(false);
    }
}

public sealed class EvidenceItemRepository : IEvidenceItemRepository
{
    private readonly OpportunityDiscoveryContext _context;

    public EvidenceItemRepository(OpportunityDiscoveryContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task AddRangeDedupAsync(IReadOnlyList<EvidenceItem> evidence, CancellationToken cancellationToken = default)
    {
        if (evidence.Count == 0)
        {
            return;
        }

        var runId = evidence[0].ResearchRunId;
        if (evidence.Any(item => item.ResearchRunId != runId))
        {
            throw new ArgumentException("Evidence batch must belong to one research run.", nameof(evidence));
        }

        var hashes = evidence.Select(item => item.ContentHash).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var existing = await _context.EvidenceItems
            .Where(item => item.ResearchRunId == runId && hashes.Contains(item.ContentHash))
            .Select(item => item.ContentHash)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var existingSet = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newItems = evidence
            .GroupBy(item => item.ContentHash, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Where(item => !existingSet.Contains(item.ContentHash))
            .ToList();
        if (newItems.Count == 0)
        {
            return;
        }

        _context.EvidenceItems.AddRange(newItems);
        await _context.Commit().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EvidenceItem>> GetByRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        return await _context.EvidenceItems
            .Where(item => item.ResearchRunId == runId)
            .OrderByDescending(item => item.PublishedAtUtc ?? item.ObservedAtUtc)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EvidenceItem>> GetByIdsAsync(IReadOnlyList<Guid> evidenceIds, CancellationToken cancellationToken = default)
    {
        if (evidenceIds.Count == 0)
        {
            return Array.Empty<EvidenceItem>();
        }

        return await _context.EvidenceItems
            .Where(item => evidenceIds.Contains(item.Id))
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

public sealed class MarketOpportunityRepository : IMarketOpportunityRepository
{
    private readonly OpportunityDiscoveryContext _context;

    public MarketOpportunityRepository(OpportunityDiscoveryContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task AddRangeAsync(IReadOnlyList<MarketOpportunity> opportunities, CancellationToken cancellationToken = default)
    {
        if (opportunities.Count == 0)
        {
            return;
        }

        _context.MarketOpportunities.AddRange(opportunities);
        await _context.Commit().ConfigureAwait(false);
    }

    public async Task<MarketOpportunity?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.MarketOpportunities
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MarketOpportunity>> ListAsync(
        OpportunityStatus? status,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var query = _context.MarketOpportunities.AsQueryable();
        if (status.HasValue)
        {
            query = query.Where(item => item.Status == status.Value);
        }

        return await query
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(Math.Clamp(limit, 1, 500))
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MarketOpportunity>> ListPublishedAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return await _context.MarketOpportunities
            .Where(item => item.Status == OpportunityStatus.Published && item.ValidUntilUtc > now)
            .OrderByDescending(item => item.Edge)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MarketOpportunity>> ListExpirableAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        return await _context.MarketOpportunities
            .Where(item => item.ValidUntilUtc <= now
                && item.Status != OpportunityStatus.Expired
                && item.Status != OpportunityStatus.Rejected)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpdateAsync(MarketOpportunity opportunity, CancellationToken cancellationToken = default)
    {
        _context.MarketOpportunities.Update(opportunity);
        await _context.Commit().ConfigureAwait(false);
    }
}

public sealed class OpportunityReviewRepository : IOpportunityReviewRepository
{
    private readonly OpportunityDiscoveryContext _context;

    public OpportunityReviewRepository(OpportunityDiscoveryContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task AddAsync(OpportunityReview review, CancellationToken cancellationToken = default)
    {
        _context.OpportunityReviews.Add(review);
        await _context.Commit().ConfigureAwait(false);
    }
}
