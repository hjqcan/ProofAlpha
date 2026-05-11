using Autotrade.SelfImprove.Application;
using Autotrade.SelfImprove.Domain.Entities;
using Autotrade.SelfImprove.Domain.Shared.Enums;
using Autotrade.SelfImprove.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Autotrade.SelfImprove.Infra.Data.Repositories;

public sealed class ImprovementRunRepository : IImprovementRunRepository
{
    private readonly SelfImproveContext _context;

    public ImprovementRunRepository(SelfImproveContext context) => _context = context;

    public async Task AddAsync(ImprovementRun run, CancellationToken cancellationToken = default)
    {
        _context.ImprovementRuns.Add(run);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<ImprovementRun?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _context.ImprovementRuns.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<ImprovementRun>> ListAsync(int limit, CancellationToken cancellationToken = default)
    {
        return await _context.ImprovementRuns.AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(Math.Max(1, limit))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpdateAsync(ImprovementRun run, CancellationToken cancellationToken = default)
    {
        _context.ImprovementRuns.Update(run);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class StrategyEpisodeRepository : IStrategyEpisodeRepository
{
    private readonly SelfImproveContext _context;

    public StrategyEpisodeRepository(SelfImproveContext context) => _context = context;

    public async Task AddAsync(StrategyEpisode episode, CancellationToken cancellationToken = default)
    {
        _context.StrategyEpisodes.Add(episode);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<StrategyEpisode?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _context.StrategyEpisodes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }
}

public sealed class StrategyMemoryRepository : IStrategyMemoryRepository
{
    private readonly SelfImproveContext _context;

    public StrategyMemoryRepository(SelfImproveContext context) => _context = context;

    public Task<StrategyMemory?> GetByStrategyIdAsync(string strategyId, CancellationToken cancellationToken = default)
    {
        return _context.StrategyMemories.FirstOrDefaultAsync(x => x.StrategyId == strategyId, cancellationToken);
    }

    public async Task UpsertAsync(StrategyMemory memory, CancellationToken cancellationToken = default)
    {
        var existing = await _context.StrategyMemories
            .FirstOrDefaultAsync(x => x.StrategyId == memory.StrategyId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            _context.StrategyMemories.Add(memory);
        }
        else
        {
            existing.Update(memory.MemoryJson, memory.PlaybookJson);
            _context.StrategyMemories.Update(existing);
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class ImprovementProposalRepository : IImprovementProposalRepository
{
    private readonly SelfImproveContext _context;

    public ImprovementProposalRepository(SelfImproveContext context) => _context = context;

    public async Task AddRangeAsync(IEnumerable<ImprovementProposal> proposals, CancellationToken cancellationToken = default)
    {
        var list = proposals.ToList();
        if (list.Count == 0)
        {
            return;
        }

        _context.ImprovementProposals.AddRange(list);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<ImprovementProposal?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _context.ImprovementProposals.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<ImprovementProposal>> GetByRunIdAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        return await _context.ImprovementProposals.AsNoTracking()
            .Where(x => x.RunId == runId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpdateAsync(ImprovementProposal proposal, CancellationToken cancellationToken = default)
    {
        _context.ImprovementProposals.Update(proposal);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class ParameterPatchRepository : IParameterPatchRepository
{
    private readonly SelfImproveContext _context;

    public ParameterPatchRepository(SelfImproveContext context) => _context = context;

    public async Task AddRangeAsync(IEnumerable<ParameterPatch> patches, CancellationToken cancellationToken = default)
    {
        var list = patches.ToList();
        if (list.Count == 0)
        {
            return;
        }

        _context.ParameterPatches.AddRange(list);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ParameterPatch>> GetByProposalIdAsync(Guid proposalId, CancellationToken cancellationToken = default)
    {
        return await _context.ParameterPatches.AsNoTracking()
            .Where(x => x.ProposalId == proposalId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

public sealed class GeneratedStrategyVersionRepository : IGeneratedStrategyVersionRepository
{
    private readonly SelfImproveContext _context;

    public GeneratedStrategyVersionRepository(SelfImproveContext context) => _context = context;

    public async Task AddAsync(GeneratedStrategyVersion version, CancellationToken cancellationToken = default)
    {
        _context.GeneratedStrategyVersions.Add(version);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<GeneratedStrategyVersion?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _context.GeneratedStrategyVersions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<GeneratedStrategyVersion>> GetActiveCanariesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.GeneratedStrategyVersions
            .Where(x => x.IsActiveCanary && x.Stage == GeneratedStrategyStage.LiveCanary)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GeneratedStrategyVersion>> GetRegistrableAsync(CancellationToken cancellationToken = default)
    {
        return await _context.GeneratedStrategyVersions.AsNoTracking()
            .Where(x => x.Stage == GeneratedStrategyStage.PaperRunning
                || x.Stage == GeneratedStrategyStage.LiveCanary
                || x.Stage == GeneratedStrategyStage.Promoted)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpdateAsync(GeneratedStrategyVersion version, CancellationToken cancellationToken = default)
    {
        _context.GeneratedStrategyVersions.Update(version);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class PromotionGateResultRepository : IPromotionGateResultRepository
{
    private readonly SelfImproveContext _context;

    public PromotionGateResultRepository(SelfImproveContext context) => _context = context;

    public async Task AddAsync(PromotionGateResult result, CancellationToken cancellationToken = default)
    {
        _context.PromotionGateResults.Add(result);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PromotionGateResult>> GetByGeneratedStrategyVersionIdAsync(
        Guid generatedStrategyVersionId,
        CancellationToken cancellationToken = default)
    {
        return await _context.PromotionGateResults.AsNoTracking()
            .Where(x => x.GeneratedStrategyVersionId == generatedStrategyVersionId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

public sealed class PatchOutcomeRepository : IPatchOutcomeRepository
{
    private readonly SelfImproveContext _context;

    public PatchOutcomeRepository(SelfImproveContext context) => _context = context;

    public async Task AddAsync(PatchOutcome outcome, CancellationToken cancellationToken = default)
    {
        _context.PatchOutcomes.Add(outcome);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PatchOutcome>> GetByProposalIdAsync(Guid proposalId, CancellationToken cancellationToken = default)
    {
        return await _context.PatchOutcomes.AsNoTracking()
            .Where(x => x.ProposalId == proposalId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
