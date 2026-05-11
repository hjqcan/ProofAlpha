using Autotrade.Strategy.Application.Persistence;
using Autotrade.Strategy.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Autotrade.Strategy.Infra.Data.Repositories;

public sealed class StrategyMaintenanceRepository : IStrategyMaintenanceRepository
{
    private readonly StrategyContext _context;

    public StrategyMaintenanceRepository(StrategyContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<int> CleanupAsync(
        DateTimeOffset decisionCutoffUtc,
        DateTimeOffset auditCutoffUtc,
        CancellationToken cancellationToken = default)
    {
        var deleted = 0;

        deleted += await _context.StrategyDecisionLogs
            .Where(x => x.CreatedAtUtc < decisionCutoffUtc)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        deleted += await _context.CommandAuditLogs
            .Where(x => x.CreatedAtUtc < auditCutoffUtc)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return deleted;
    }
}
