namespace Autotrade.Domain.Abstractions.EventBus;

/// <summary>
/// Marks a domain event class as an integration event without requiring the marker interface.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class IntegrationEventAttribute : Attribute
{
    public IntegrationEventAttribute(string eventName, string version = "v1")
    {
        if (string.IsNullOrWhiteSpace(eventName))
        {
            throw new ArgumentException("Event name cannot be empty.", nameof(eventName));
        }

        EventName = eventName;
        Version = version;
    }

    /// <summary>
    /// Gets the message route name, formatted as BoundedContext.Aggregate.Action.
    /// </summary>
    public string EventName { get; }

    /// <summary>
    /// Gets the event contract version.
    /// </summary>
    public string Version { get; }
}
