namespace Autotrade.Strategy.Application.Persistence;

public interface IStrategyUnitOfWork
{
    Task<bool> CommitAsync(CancellationToken cancellationToken = default);
}
