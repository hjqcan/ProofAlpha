namespace Autotrade.Domain.Abstractions.EventBus;

/// <summary>
/// Marks a domain event as an integration event that can be published across bounded contexts.
/// </summary>
public interface IIntegrationEvent
{
    /// <summary>
    /// Gets the message route name, formatted as BoundedContext.Aggregate.Action.
    /// </summary>
    string EventName { get; }

    /// <summary>
    /// Gets the event contract version.
    /// </summary>
    string Version { get; }
}
