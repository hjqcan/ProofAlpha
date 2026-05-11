using Autotrade.Strategy.Domain.Entities;

namespace Autotrade.Strategy.Application.RunSessions;

public interface IPaperRunSessionRepository
{
    Task<PaperRunSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<PaperRunSession?> GetActiveAsync(string executionMode, CancellationToken cancellationToken = default);

    Task AddAsync(PaperRunSession session, CancellationToken cancellationToken = default);

    Task UpdateAsync(PaperRunSession session, CancellationToken cancellationToken = default);
}
