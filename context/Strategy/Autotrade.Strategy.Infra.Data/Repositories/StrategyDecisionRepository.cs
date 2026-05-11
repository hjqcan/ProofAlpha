using Autotrade.Strategy.Application.Decisions;
using Autotrade.Strategy.Domain.Entities;
using Autotrade.Strategy.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Autotrade.Strategy.Infra.Data.Repositories;

public sealed class StrategyDecisionRepository : IStrategyDecisionRepository
{
    private readonly StrategyContext _context;

    public StrategyDecisionRepository(StrategyContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task AddAsync(StrategyDecisionLog log, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(log);
        _context.StrategyDecisionLogs.Add(log);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StrategyDecisionLog>> QueryAsync(
        StrategyDecisionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var q = _context.StrategyDecisionLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.StrategyId))
        {
            q = q.Where(x => x.StrategyId == query.StrategyId);
        }

        if (!string.IsNullOrWhiteSpace(query.MarketId))
        {
            q = q.Where(x => x.MarketId == query.MarketId);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            q = q.Where(x => x.Action == query.Action);
        }

        if (!string.IsNullOrWhiteSpace(query.CorrelationId))
        {
            q = q.Where(x => x.CorrelationId == query.CorrelationId);
        }

        if (query.RunSessionId.HasValue)
        {
            q = q.Where(x => x.RunSessionId == query.RunSessionId.Value);
        }

        if (query.FromUtc.HasValue)
        {
            q = q.Where(x => x.CreatedAtUtc >= query.FromUtc.Value);
        }

        if (query.ToUtc.HasValue)
        {
            q = q.Where(x => x.CreatedAtUtc <= query.ToUtc.Value);
        }

        q = q.OrderByDescending(x => x.CreatedAtUtc).Take(Math.Max(1, query.Limit));

        return await q.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<StrategyDecisionLog?> GetAsync(Guid decisionId, CancellationToken cancellationToken = default)
    {
        if (decisionId == Guid.Empty)
        {
            return Task.FromResult<StrategyDecisionLog?>(null);
        }

        return _context.StrategyDecisionLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == decisionId, cancellationToken);
    }
}
