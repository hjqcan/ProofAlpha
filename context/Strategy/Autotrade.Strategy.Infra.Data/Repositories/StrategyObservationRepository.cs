using Autotrade.Strategy.Application.Observations;
using Autotrade.Strategy.Domain.Entities;
using Autotrade.Strategy.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Autotrade.Strategy.Infra.Data.Repositories;

public sealed class StrategyObservationRepository : IStrategyObservationRepository
{
    private readonly StrategyContext _context;

    public StrategyObservationRepository(StrategyContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task AddAsync(StrategyObservationLog observation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        _context.StrategyObservationLogs.Add(observation);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AddRangeAsync(
        IEnumerable<StrategyObservationLog> observations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observations);
        var list = observations.ToList();
        if (list.Count == 0)
        {
            return;
        }

        _context.StrategyObservationLogs.AddRange(list);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StrategyObservationLog>> QueryAsync(
        StrategyObservationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var q = _context.StrategyObservationLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.StrategyId))
        {
            q = q.Where(x => x.StrategyId == query.StrategyId);
        }

        if (!string.IsNullOrWhiteSpace(query.MarketId))
        {
            q = q.Where(x => x.MarketId == query.MarketId);
        }

        if (!string.IsNullOrWhiteSpace(query.Phase))
        {
            q = q.Where(x => x.Phase == query.Phase);
        }

        if (!string.IsNullOrWhiteSpace(query.Outcome))
        {
            q = q.Where(x => x.Outcome == query.Outcome);
        }

        if (!string.IsNullOrWhiteSpace(query.ReasonCode))
        {
            q = q.Where(x => x.ReasonCode == query.ReasonCode);
        }

        if (!string.IsNullOrWhiteSpace(query.ConfigVersion))
        {
            q = q.Where(x => x.ConfigVersion == query.ConfigVersion);
        }

        if (query.FromUtc.HasValue)
        {
            q = q.Where(x => x.CreatedAtUtc >= query.FromUtc.Value);
        }

        if (query.ToUtc.HasValue)
        {
            q = q.Where(x => x.CreatedAtUtc <= query.ToUtc.Value);
        }

        return await q.OrderByDescending(x => x.CreatedAtUtc)
            .Take(Math.Max(1, query.Limit))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
