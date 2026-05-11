using Autotrade.Strategy.Application.Audit;
using Autotrade.Strategy.Domain.Entities;
using Autotrade.Strategy.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Autotrade.Strategy.Infra.Data.Repositories;

public sealed class CommandAuditRepository : ICommandAuditRepository
{
    private readonly StrategyContext _context;

    public CommandAuditRepository(StrategyContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task AddAsync(CommandAuditLog log, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(log);
        _context.CommandAuditLogs.Add(log);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CommandAuditLog>> QueryAsync(
        CommandAuditQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var q = _context.CommandAuditLogs.AsNoTracking().AsQueryable();

        if (query.FromUtc.HasValue)
        {
            q = q.Where(item => item.CreatedAtUtc >= query.FromUtc.Value);
        }

        if (query.ToUtc.HasValue)
        {
            q = q.Where(item => item.CreatedAtUtc <= query.ToUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.CommandName))
        {
            q = q.Where(item => item.CommandName == query.CommandName);
        }

        if (!string.IsNullOrWhiteSpace(query.Actor))
        {
            q = q.Where(item => item.Actor == query.Actor);
        }

        return await q.OrderByDescending(item => item.CreatedAtUtc)
            .Take(Math.Clamp(query.Limit, 1, 5000))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
