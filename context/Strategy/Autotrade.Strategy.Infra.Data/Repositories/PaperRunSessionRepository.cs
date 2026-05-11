using Autotrade.Strategy.Application.RunSessions;
using Autotrade.Strategy.Domain.Entities;
using Autotrade.Strategy.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Autotrade.Strategy.Infra.Data.Repositories;

public sealed class PaperRunSessionRepository(StrategyContext context) : IPaperRunSessionRepository
{
    private readonly StrategyContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public Task<PaperRunSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            return Task.FromResult<PaperRunSession?>(null);
        }

        return _context.PaperRunSessions
            .FirstOrDefaultAsync(session => session.Id == sessionId, cancellationToken);
    }

    public Task<PaperRunSession?> GetActiveAsync(
        string executionMode,
        CancellationToken cancellationToken = default)
    {
        return _context.PaperRunSessions
            .Where(session => session.ExecutionMode == executionMode && session.StoppedAtUtc == null)
            .OrderByDescending(session => session.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(PaperRunSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await _context.PaperRunSessions.AddAsync(session, cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(PaperRunSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        _context.PaperRunSessions.Update(session);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
