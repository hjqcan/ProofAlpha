using Autotrade.Domain.Abstractions.EventBus;
using Microsoft.EntityFrameworkCore;
using NetDevPack.Messaging;

namespace Autotrade.Infra.Data.Core.EventBus;

/// <summary>
/// Publishes integration events inside the same transaction boundary as an EF Core save.
/// </summary>
public interface ITransactionalIntegrationEventPublisher : IIntegrationEventPublisher
{
    int SaveChangesAndPublish(
        DbContext dbContext,
        IReadOnlyCollection<Event> domainEvents,
        Func<int> saveChanges);

    Task<int> SaveChangesAndPublishAsync(
        DbContext dbContext,
        IReadOnlyCollection<Event> domainEvents,
        Func<CancellationToken, Task<int>> saveChangesAsync,
        CancellationToken cancellationToken = default);
}
