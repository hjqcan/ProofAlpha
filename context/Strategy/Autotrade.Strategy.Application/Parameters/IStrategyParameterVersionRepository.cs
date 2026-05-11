using Autotrade.Strategy.Domain.Entities;

namespace Autotrade.Strategy.Application.Parameters;

public interface IStrategyParameterVersionRepository
{
    Task AddAsync(StrategyParameterVersion version, CancellationToken cancellationToken = default);

    Task<StrategyParameterVersion?> GetAsync(Guid versionId, CancellationToken cancellationToken = default);

    Task<StrategyParameterVersion?> GetLatestAsync(string strategyId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StrategyParameterVersion>> GetLatestByStrategyIdsAsync(
        IReadOnlyCollection<string> strategyIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StrategyParameterVersion>> GetRecentAsync(
        string strategyId,
        int limit,
        CancellationToken cancellationToken = default);
}
