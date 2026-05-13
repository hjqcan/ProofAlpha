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

public sealed class SourceProfileRepository : ISourceProfileRepository
{
    private readonly OpportunityDiscoveryContext _context;

    public SourceProfileRepository(OpportunityDiscoveryContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task AddAsync(SourceProfile profile, CancellationToken cancellationToken = default)
    {
        _context.SourceProfiles.Add(profile);
        await _context.Commit().ConfigureAwait(false);
    }

    public async Task<SourceProfile?> GetLatestByKeyAsync(string sourceKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            return null;
        }

        return await _context.SourceProfiles
            .Where(item => item.SourceKey == sourceKey.Trim().ToLowerInvariant())
            .OrderByDescending(item => item.Version)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, SourceProfile>> GetCurrentByKeysAsync(
        IReadOnlyList<string> sourceKeys,
        CancellationToken cancellationToken = default)
    {
        var keys = sourceKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (keys.Count == 0)
        {
            return new Dictionary<string, SourceProfile>(StringComparer.OrdinalIgnoreCase);
        }

        var profiles = await _context.SourceProfiles
            .Where(item => keys.Contains(item.SourceKey))
            .OrderByDescending(item => item.Version)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return profiles
            .GroupBy(item => item.SourceKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.Version).First(),
                StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<SourceProfile>> ListCurrentAsync(CancellationToken cancellationToken = default)
    {
        var profiles = await _context.SourceProfiles
            .OrderBy(item => item.SourceKey)
            .ThenByDescending(item => item.Version)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return profiles
            .GroupBy(item => item.SourceKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.Version).First())
            .OrderBy(item => item.SourceKey)
            .ToList();
    }
}

public sealed class SourceObservationRepository : ISourceObservationRepository
{
    private readonly OpportunityDiscoveryContext _context;

    public SourceObservationRepository(OpportunityDiscoveryContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task AddAsync(SourceObservation observation, CancellationToken cancellationToken = default)
    {
        _context.SourceObservations.Add(observation);
        await _context.Commit().ConfigureAwait(false);
    }
}

public sealed class EvidenceSnapshotRepository : IEvidenceSnapshotRepository
{
    private readonly OpportunityDiscoveryContext _context;

    public EvidenceSnapshotRepository(OpportunityDiscoveryContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task AddAsync(EvidenceSnapshotBundle bundle, CancellationToken cancellationToken = default)
    {
        await AddRangeAsync([bundle], cancellationToken).ConfigureAwait(false);
    }

    public async Task AddRangeAsync(IReadOnlyList<EvidenceSnapshotBundle> bundles, CancellationToken cancellationToken = default)
    {
        if (bundles.Count == 0)
        {
            return;
        }

        _context.EvidenceSnapshots.AddRange(bundles.Select(bundle => bundle.Snapshot));
        _context.EvidenceCitations.AddRange(bundles.SelectMany(bundle => bundle.Citations));
        _context.EvidenceConflicts.AddRange(bundles.SelectMany(bundle => bundle.Conflicts));
        _context.OfficialConfirmations.AddRange(bundles.SelectMany(bundle => bundle.OfficialConfirmations));
        await _context.Commit().ConfigureAwait(false);
    }

    public async Task<EvidenceSnapshotBundle?> GetForOpportunityAsOfAsync(
        Guid opportunityId,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _context.EvidenceSnapshots
            .Where(item => item.OpportunityId == opportunityId && item.SnapshotAsOfUtc <= asOfUtc)
            .OrderByDescending(item => item.SnapshotAsOfUtc)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (snapshot is null)
        {
            return null;
        }

        var citations = await _context.EvidenceCitations
            .Where(item => item.EvidenceSnapshotId == snapshot.Id)
            .OrderByDescending(item => item.RelevanceScore)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var conflicts = await _context.EvidenceConflicts
            .Where(item => item.EvidenceSnapshotId == snapshot.Id)
            .OrderByDescending(item => item.Severity)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var confirmations = await _context.OfficialConfirmations
            .Where(item => item.EvidenceSnapshotId == snapshot.Id)
            .OrderByDescending(item => item.Confidence)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new EvidenceSnapshotBundle(snapshot, citations, conflicts, confirmations);
    }
}

public sealed class OpportunityV2Repository : IOpportunityV2Repository
{
    private readonly OpportunityDiscoveryContext _context;

    public OpportunityV2Repository(OpportunityDiscoveryContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<OpportunityHypothesis?> GetHypothesisAsync(
        Guid hypothesisId,
        CancellationToken cancellationToken = default)
    {
        if (hypothesisId == Guid.Empty)
        {
            return null;
        }

        return await _context.OpportunityHypotheses
            .FirstOrDefaultAsync(item => item.Id == hypothesisId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddHypothesisAsync(OpportunityHypothesis hypothesis, CancellationToken cancellationToken = default)
    {
        _context.OpportunityHypotheses.Add(hypothesis);
        await _context.Commit().ConfigureAwait(false);
    }

    public async Task UpdateHypothesisAsync(OpportunityHypothesis hypothesis, CancellationToken cancellationToken = default)
    {
        _context.OpportunityHypotheses.Update(hypothesis);
        await _context.Commit().ConfigureAwait(false);
    }

    public async Task AddTransitionAsync(OpportunityLifecycleTransition transition, CancellationToken cancellationToken = default)
    {
        _context.OpportunityLifecycleTransitions.Add(transition);
        await _context.Commit().ConfigureAwait(false);
    }

    public async Task UpdateHypothesisWithTransitionAsync(
        OpportunityHypothesis hypothesis,
        OpportunityLifecycleTransition transition,
        CancellationToken cancellationToken = default)
    {
        _context.OpportunityHypotheses.Update(hypothesis);
        _context.OpportunityLifecycleTransitions.Add(transition);
        await _context.Commit().ConfigureAwait(false);
    }

    public async Task AddFeatureSnapshotAsync(OpportunityFeatureSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        _context.OpportunityFeatureSnapshots.Add(snapshot);
        await _context.Commit().ConfigureAwait(false);
    }

    public async Task AddScoreAsync(OpportunityScore score, CancellationToken cancellationToken = default)
    {
        _context.OpportunityScores.Add(score);
        await _context.Commit().ConfigureAwait(false);
    }

    public async Task AddFeatureSnapshotAndScoreAsync(
        OpportunityFeatureSnapshot snapshot,
        OpportunityScore score,
        CancellationToken cancellationToken = default)
    {
        _context.OpportunityFeatureSnapshots.Add(snapshot);
        _context.OpportunityScores.Add(score);
        await _context.Commit().ConfigureAwait(false);
    }

    public async Task<OpportunityFeatureSnapshot?> GetLatestFeatureSnapshotAsync(
        Guid hypothesisId,
        CancellationToken cancellationToken = default)
    {
        if (hypothesisId == Guid.Empty)
        {
            return null;
        }

        return await _context.OpportunityFeatureSnapshots
            .Where(item => item.HypothesisId == hypothesisId)
            .OrderByDescending(item => item.CreatedAtUtc)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OpportunityScore?> GetLatestScoreAsync(
        Guid hypothesisId,
        CancellationToken cancellationToken = default)
    {
        if (hypothesisId == Guid.Empty)
        {
            return null;
        }

        return await _context.OpportunityScores
            .Where(item => item.HypothesisId == hypothesisId)
            .OrderByDescending(item => item.CreatedAtUtc)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddEvaluationRunAsync(OpportunityEvaluationRun run, CancellationToken cancellationToken = default)
    {
        _context.OpportunityEvaluationRuns.Add(run);
        await _context.Commit().ConfigureAwait(false);
    }

    public async Task UpdateEvaluationRunAsync(OpportunityEvaluationRun run, CancellationToken cancellationToken = default)
    {
        _context.OpportunityEvaluationRuns.Update(run);
        await _context.Commit().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OpportunityEvaluationRun>> ListEvaluationRunsAsync(
        Guid hypothesisId,
        CancellationToken cancellationToken = default)
    {
        return await _context.OpportunityEvaluationRuns
            .Where(item => item.HypothesisId == hypothesisId)
            .OrderByDescending(item => item.StartedAtUtc)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddPromotionGateAsync(OpportunityPromotionGate gate, CancellationToken cancellationToken = default)
    {
        _context.OpportunityPromotionGates.Add(gate);
        await _context.Commit().ConfigureAwait(false);
    }

    public async Task AddEvaluationRunAndGateAsync(
        OpportunityEvaluationRun run,
        OpportunityPromotionGate gate,
        CancellationToken cancellationToken = default)
    {
        _context.OpportunityEvaluationRuns.Add(run);
        _context.OpportunityPromotionGates.Add(gate);
        await _context.Commit().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OpportunityPromotionGate>> ListPromotionGatesAsync(
        Guid hypothesisId,
        CancellationToken cancellationToken = default)
    {
        return await _context.OpportunityPromotionGates
            .Where(item => item.HypothesisId == hypothesisId)
            .OrderByDescending(item => item.EvaluatedAtUtc)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddExecutablePolicyAsync(ExecutableOpportunityPolicy policy, CancellationToken cancellationToken = default)
    {
        _context.ExecutableOpportunityPolicies.Add(policy);
        await _context.Commit().ConfigureAwait(false);
    }

    public async Task<ExecutableOpportunityPolicy?> GetExecutablePolicyAsync(
        Guid policyId,
        CancellationToken cancellationToken = default)
    {
        if (policyId == Guid.Empty)
        {
            return null;
        }

        return await _context.ExecutableOpportunityPolicies
            .FirstOrDefaultAsync(item => item.Id == policyId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpdateExecutablePolicyAsync(ExecutableOpportunityPolicy policy, CancellationToken cancellationToken = default)
    {
        _context.ExecutableOpportunityPolicies.Update(policy);
        await _context.Commit().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ExecutableOpportunityPolicy>> ListExecutablePoliciesAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return await _context.ExecutableOpportunityPolicies
            .Where(item => item.Status == ExecutableOpportunityPolicyStatus.Active
                && item.ValidFromUtc <= now
                && item.ValidUntilUtc > now)
            .OrderByDescending(item => item.Edge)
            .Take(Math.Clamp(limit, 1, 500))
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ExecutableOpportunityPolicy?> GetActiveExecutablePolicyForHypothesisAsync(
        Guid hypothesisId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (hypothesisId == Guid.Empty)
        {
            return null;
        }

        return await _context.ExecutableOpportunityPolicies
            .Where(item => item.HypothesisId == hypothesisId
                && item.Status == ExecutableOpportunityPolicyStatus.Active
                && item.ValidFromUtc <= now
                && item.ValidUntilUtc > now)
            .OrderByDescending(item => item.UpdatedAtUtc)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ExecutableOpportunityPolicyFeedItem>> ListExecutablePolicyFeedItemsAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var requestedLimit = Math.Clamp(limit, 1, 500);
        var candidates = await (
                from policy in _context.ExecutableOpportunityPolicies.AsNoTracking()
                join hypothesis in _context.OpportunityHypotheses.AsNoTracking()
                    on policy.HypothesisId equals hypothesis.Id
                join allocation in _context.OpportunityLiveAllocations.AsNoTracking()
                    on policy.Id equals allocation.ExecutablePolicyId
                where policy.Status == ExecutableOpportunityPolicyStatus.Active
                    && policy.ValidFromUtc <= now
                    && policy.ValidUntilUtc > now
                    && hypothesis.Status == OpportunityHypothesisStatus.LivePublished
                    && hypothesis.ActivePolicyId == policy.Id
                    && hypothesis.ActiveLiveAllocationId == allocation.Id
                    && allocation.Status == OpportunityLiveAllocationStatus.Active
                    && allocation.ValidUntilUtc > now
                orderby policy.Edge descending
                select new
                {
                    Policy = policy,
                    Allocation = allocation
                })
            .Take(500)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            return Array.Empty<ExecutableOpportunityPolicyFeedItem>();
        }

        var hypothesisIds = candidates
            .Select(item => item.Policy.HypothesisId)
            .Distinct()
            .ToList();

        var latestScores = (await _context.OpportunityScores
                .AsNoTracking()
                .Where(score => hypothesisIds.Contains(score.HypothesisId) && score.CanPromote)
                .OrderByDescending(score => score.CreatedAtUtc)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .GroupBy(score => score.HypothesisId)
            .ToDictionary(group => group.Key, group => group.First());

        var latestRuns = (await _context.OpportunityEvaluationRuns
                .AsNoTracking()
                .Where(run => hypothesisIds.Contains(run.HypothesisId)
                    && run.Status == OpportunityEvaluationRunStatus.Succeeded)
                .OrderByDescending(run => run.UpdatedAtUtc)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .GroupBy(run => run.HypothesisId)
            .ToDictionary(group => group.Key, group => group.First());

        var latestGates = (await _context.OpportunityPromotionGates
                .AsNoTracking()
                .Where(gate => hypothesisIds.Contains(gate.HypothesisId))
                .OrderByDescending(gate => gate.EvaluatedAtUtc)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .GroupBy(gate => gate.HypothesisId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .GroupBy(gate => gate.GateKind)
                    .ToDictionary(
                        gateGroup => gateGroup.Key,
                        gateGroup => gateGroup.First()));

        var result = new List<ExecutableOpportunityPolicyFeedItem>(requestedLimit);
        foreach (var candidate in candidates)
        {
            var hypothesisId = candidate.Policy.HypothesisId;
            if (!latestScores.TryGetValue(hypothesisId, out var score) ||
                !latestRuns.TryGetValue(hypothesisId, out var run) ||
                !latestGates.TryGetValue(hypothesisId, out var gatesByKind) ||
                !HasCurrentPassedLiveGates(gatesByKind))
            {
                continue;
            }

            result.Add(new ExecutableOpportunityPolicyFeedItem(
                candidate.Policy,
                score.Id,
                run.Id,
                candidate.Allocation.Id,
                score.ScoreVersion,
                candidate.Allocation.MaxNotional,
                candidate.Allocation.MaxContracts));

            if (result.Count >= requestedLimit)
            {
                break;
            }
        }

        return result;
    }

    public async Task AddLiveAllocationAsync(OpportunityLiveAllocation allocation, CancellationToken cancellationToken = default)
    {
        _context.OpportunityLiveAllocations.Add(allocation);
        await _context.Commit().ConfigureAwait(false);
    }

    public async Task<OpportunityLiveAllocation?> GetLiveAllocationAsync(
        Guid allocationId,
        CancellationToken cancellationToken = default)
    {
        if (allocationId == Guid.Empty)
        {
            return null;
        }

        return await _context.OpportunityLiveAllocations
            .FirstOrDefaultAsync(item => item.Id == allocationId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OpportunityLiveAllocation>> ListActiveLiveAllocationsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        return await _context.OpportunityLiveAllocations
            .Where(item => item.Status == OpportunityLiveAllocationStatus.Active && item.ValidUntilUtc > now)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OpportunityLiveAllocation?> GetActiveLiveAllocationForHypothesisAsync(
        Guid hypothesisId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (hypothesisId == Guid.Empty)
        {
            return null;
        }

        return await _context.OpportunityLiveAllocations
            .Where(item => item.HypothesisId == hypothesisId
                && item.Status == OpportunityLiveAllocationStatus.Active
                && item.ValidUntilUtc > now)
            .OrderByDescending(item => item.UpdatedAtUtc)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OpportunityHypothesis>> ListLiveHypothesesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.OpportunityHypotheses
            .Where(item => item.Status == OpportunityHypothesisStatus.LiveEligible
                || item.Status == OpportunityHypothesisStatus.LivePublished)
            .OrderByDescending(item => item.UpdatedAtUtc)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddLiveAllocationWithHypothesisTransitionAsync(
        OpportunityLiveAllocation allocation,
        OpportunityHypothesis hypothesis,
        OpportunityLifecycleTransition transition,
        CancellationToken cancellationToken = default)
    {
        _context.OpportunityLiveAllocations.Add(allocation);
        _context.OpportunityHypotheses.Update(hypothesis);
        _context.OpportunityLifecycleTransitions.Add(transition);
        await _context.Commit().ConfigureAwait(false);
    }

    public async Task SuspendLiveOpportunityAsync(
        OpportunityHypothesis hypothesis,
        OpportunityLifecycleTransition transition,
        ExecutableOpportunityPolicy policy,
        OpportunityLiveAllocation? allocation,
        CancellationToken cancellationToken = default)
    {
        _context.OpportunityHypotheses.Update(hypothesis);
        _context.OpportunityLifecycleTransitions.Add(transition);
        _context.ExecutableOpportunityPolicies.Update(policy);
        if (allocation is not null)
        {
            _context.OpportunityLiveAllocations.Update(allocation);
        }

        await _context.Commit().ConfigureAwait(false);
    }

    private static bool HasCurrentPassedLiveGates(
        IReadOnlyDictionary<OpportunityPromotionGateKind, OpportunityPromotionGate> gatesByKind)
        => OpportunityHypothesis.RequiredLiveGateKinds.All(kind =>
            gatesByKind.TryGetValue(kind, out var gate) &&
            gate.Status == OpportunityPromotionGateStatus.Passed);
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
