namespace Autotrade.Strategy.Application.Persistence;

public interface IStrategyMaintenanceRepository
{
    Task<int> CleanupAsync(DateTimeOffset decisionCutoffUtc, DateTimeOffset auditCutoffUtc,
        CancellationToken cancellationToken = default);
}
