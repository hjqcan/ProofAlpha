using Autotrade.Domain.Abstractions.EventBus;
using NetDevPack.Messaging;

namespace Autotrade.EventBus.CAP;

public class IntegrationDtoConverterRegistry
{
    private readonly IEnumerable<IIntegrationDtoConverter> _converters;

    public IntegrationDtoConverterRegistry(IEnumerable<IIntegrationDtoConverter> converters)
    {
        _converters = converters ?? Enumerable.Empty<IIntegrationDtoConverter>();
    }

    public object Convert(Event domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var converter = _converters.FirstOrDefault(c => c.CanConvert(domainEvent));
        if (converter is null)
        {
            throw new InvalidOperationException(
                $"No integration DTO converter registered for domain event: {domainEvent.GetType().FullName}");
        }

        return converter.Convert(domainEvent);
    }
}
