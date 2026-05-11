using Autotrade.Strategy.Application.Persistence;
using Autotrade.Strategy.Infra.Data.Context;

namespace Autotrade.Strategy.Infra.Data.Repositories;

public sealed class StrategyUnitOfWork(StrategyContext context) : IStrategyUnitOfWork
{
    public async Task<bool> CommitAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await context.Commit().ConfigureAwait(false);
    }
}
