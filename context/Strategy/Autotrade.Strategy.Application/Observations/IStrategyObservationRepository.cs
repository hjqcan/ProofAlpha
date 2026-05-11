using Autotrade.Strategy.Domain.Entities;

namespace Autotrade.Strategy.Application.Observations;

public interface IStrategyObservationRepository
{
    Task AddAsync(StrategyObservationLog observation, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IEnumerable<StrategyObservationLog> observations, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StrategyObservationLog>> QueryAsync(
        StrategyObservationQuery query,
        CancellationToken cancellationToken = default);
}
