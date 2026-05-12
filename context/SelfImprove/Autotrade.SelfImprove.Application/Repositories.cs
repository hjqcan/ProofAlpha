using Autotrade.SelfImprove.Domain.Entities;

namespace Autotrade.SelfImprove.Application;

public interface IImprovementRunRepository
{
    Task AddAsync(ImprovementRun run, CancellationToken cancellationToken = default);

    Task<ImprovementRun?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImprovementRun>> ListAsync(int limit, CancellationToken cancellationToken = default);

    Task UpdateAsync(ImprovementRun run, CancellationToken cancellationToken = default);
}

public interface IStrategyEpisodeRepository
{
    Task AddAsync(StrategyEpisode episode, CancellationToken cancellationToken = default);

    Task<StrategyEpisode?> GetAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IStrategyMemoryRepository
{
    Task<StrategyMemory?> GetByStrategyIdAsync(string strategyId, CancellationToken cancellationToken = default);

    Task UpsertAsync(StrategyMemory memory, CancellationToken cancellationToken = default);
}

public interface IImprovementProposalRepository
{
    Task AddRangeAsync(IEnumerable<ImprovementProposal> proposals, CancellationToken cancellationToken = default);

    Task<ImprovementProposal?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImprovementProposal>> GetByRunIdAsync(Guid runId, CancellationToken cancellationToken = default);

    Task UpdateAsync(ImprovementProposal proposal, CancellationToken cancellationToken = default);
}

public interface IParameterPatchRepository
{
    Task AddRangeAsync(IEnumerable<ParameterPatch> patches, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ParameterPatch>> GetByProposalIdAsync(Guid proposalId, CancellationToken cancellationToken = default);
}

public interface IGeneratedStrategyVersionRepository
{
    Task AddAsync(GeneratedStrategyVersion version, CancellationToken cancellationToken = default);

    Task<GeneratedStrategyVersion?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GeneratedStrategyVersion>> GetActiveCanariesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GeneratedStrategyVersion>> GetByProposalIdsAsync(
        IReadOnlyCollection<Guid> proposalIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GeneratedStrategyVersion>> GetRegistrableAsync(CancellationToken cancellationToken = default);

    Task UpdateAsync(GeneratedStrategyVersion version, CancellationToken cancellationToken = default);
}

public interface IPromotionGateResultRepository
{
    Task AddAsync(PromotionGateResult result, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PromotionGateResult>> GetByGeneratedStrategyVersionIdAsync(
        Guid generatedStrategyVersionId,
        CancellationToken cancellationToken = default);
}

public interface IPatchOutcomeRepository
{
    Task AddAsync(PatchOutcome outcome, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PatchOutcome>> GetByProposalIdAsync(Guid proposalId, CancellationToken cancellationToken = default);
}
