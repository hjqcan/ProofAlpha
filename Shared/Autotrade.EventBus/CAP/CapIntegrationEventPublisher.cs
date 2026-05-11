using System.Diagnostics;
using Autotrade.Domain.Abstractions.EventBus;
using Autotrade.EventBus.Converters;
using Autotrade.Infra.Data.Core.EventBus;
using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetDevPack.Messaging;

namespace Autotrade.EventBus.CAP;

/// <summary>
/// CAP-based integration event publisher.
/// </summary>
public class CapIntegrationEventPublisher : ITransactionalIntegrationEventPublisher
{
    private readonly ICapPublisher _capPublisher;
    private readonly DomainEventToIntegrationEventConverter _converter;
    private readonly IntegrationDtoConverterRegistry _dtoConverterRegistry;
    private readonly ILogger<CapIntegrationEventPublisher> _logger;

    public CapIntegrationEventPublisher(
        ICapPublisher capPublisher,
        DomainEventToIntegrationEventConverter converter,
        IntegrationDtoConverterRegistry dtoConverterRegistry,
        ILogger<CapIntegrationEventPublisher> logger)
    {
        _capPublisher = capPublisher ?? throw new ArgumentNullException(nameof(capPublisher));
        _converter = converter ?? throw new ArgumentNullException(nameof(converter));
        _dtoConverterRegistry = dtoConverterRegistry ?? throw new ArgumentNullException(nameof(dtoConverterRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public int SaveChangesAndPublish(
        DbContext dbContext,
        IReadOnlyCollection<Event> domainEvents,
        Func<int> saveChanges)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(domainEvents);
        ArgumentNullException.ThrowIfNull(saveChanges);

        using var transaction = dbContext.Database.BeginTransaction(_capPublisher, false);

        try
        {
            var savedChanges = saveChanges();
            if (savedChanges > 0)
            {
                PublishAsync(domainEvents).ConfigureAwait(false).GetAwaiter().GetResult();
            }

            transaction.Commit();
            return savedChanges;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<int> SaveChangesAndPublishAsync(
        DbContext dbContext,
        IReadOnlyCollection<Event> domainEvents,
        Func<CancellationToken, Task<int>> saveChangesAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(domainEvents);
        ArgumentNullException.ThrowIfNull(saveChangesAsync);

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(_capPublisher, false, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var savedChanges = await saveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (savedChanges > 0)
            {
                await PublishAsync(domainEvents, cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return savedChanges;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task PublishAsync(IEnumerable<Event> domainEvents, CancellationToken cancellationToken = default)
    {
        if (domainEvents is null)
        {
            return;
        }

        var correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString();
        var integrationEvents = _converter.Convert(domainEvents).ToList();

        if (integrationEvents.Count == 0)
        {
            _logger.LogDebug("No integration events to publish. CorrelationId: {CorrelationId}", correlationId);
            return;
        }

        _logger.LogInformation(
            "Publishing {Count} integration events. CorrelationId: {CorrelationId}",
            integrationEvents.Count,
            correlationId);

        foreach (var wrapper in integrationEvents)
        {
            try
            {
                var payload = _dtoConverterRegistry.Convert(wrapper.Payload);
                var headers = wrapper.BuildHeaders(correlationId);

                _logger.LogInformation(
                    "Publishing integration event: {EventName} v{Version}, AggregateId: {AggregateId}, PayloadType: {PayloadType}",
                    wrapper.EventName,
                    wrapper.Version,
                    wrapper.Payload.AggregateId,
                    payload.GetType().Name);

                await _capPublisher.PublishAsync(
                        wrapper.EventName,
                        payload,
                        headers,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Integration event publish failed: {EventName} v{Version}, CorrelationId: {CorrelationId}",
                    wrapper.EventName,
                    wrapper.Version,
                    correlationId);
                throw;
            }
        }
    }
}
