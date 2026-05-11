using NetDevPack.Messaging;

namespace Autotrade.Domain.Abstractions.EventBus;

/// <summary>
/// Converts internal domain events into pure integration DTO payloads.
/// </summary>
public interface IIntegrationDtoConverter
{
    /// <summary>
    /// Returns true when this converter supports the supplied domain event.
    /// </summary>
    bool CanConvert(Event domainEvent);

    /// <summary>
    /// Converts a supported domain event to its integration DTO.
    /// </summary>
    object Convert(Event domainEvent);
}
