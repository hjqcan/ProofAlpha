using NetDevPack.Messaging;

namespace Autotrade.Domain.Abstractions.EventBus;

/// <summary>
/// Publishes integration events derived from domain events.
/// </summary>
public interface IIntegrationEventPublisher
{
    /// <summary>
    /// Publishes the integration events contained in the supplied domain event collection.
    /// </summary>
    Task PublishAsync(IEnumerable<Event> domainEvents, CancellationToken cancellationToken = default);
}
