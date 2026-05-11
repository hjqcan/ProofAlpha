using System.Diagnostics;
using Autotrade.Domain.Abstractions.EventBus;
using Autotrade.Infra.Data.Core.EventBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetDevPack.Data;
using NetDevPack.Domain;
using NetDevPack.Messaging;

namespace Autotrade.Infra.Data.Core.Context;

/// <summary>
/// Base DbContext that saves aggregate changes and dispatches domain events through one path.
/// </summary>
public abstract class BaseDbContext : DbContext, IUnitOfWork
{
    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private readonly IIntegrationEventPublisher? _integrationEventPublisher;
    private readonly ILogger<BaseDbContext> _logger;

    protected BaseDbContext(
        DbContextOptions options,
        IDomainEventDispatcher domainEventDispatcher,
        IIntegrationEventPublisher? integrationEventPublisher,
        ILogger<BaseDbContext> logger) : base(options)
    {
        _domainEventDispatcher = domainEventDispatcher ?? throw new ArgumentNullException(nameof(domainEventDispatcher));
        _integrationEventPublisher = integrationEventPublisher;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> Commit()
    {
        try
        {
            var savedChanges = await SaveChangesAsync().ConfigureAwait(false);
            if (savedChanges <= 0)
            {
                _logger.LogWarning("No data changes were saved; domain events were not dispatched.");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unit of work commit failed.");
            throw;
        }
    }

    public override int SaveChanges()
    {
        return SaveChanges(acceptAllChangesOnSuccess: true);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        DetectChangesIfNeeded();

        var domainEventBatch = CollectDomainEventBatch();
        var savedChanges = SaveChangesWithIntegrationEvents(
            domainEventBatch.Events,
            acceptAllChangesOnSuccess,
            () => base.SaveChanges(acceptAllChangesOnSuccess),
            () => base.SaveChanges(acceptAllChangesOnSuccess: false));

        ClearDomainEventsAfterSuccessfulSave(domainEventBatch.Entities, savedChanges);

        DispatchLocalDomainEventsAfterSaveAsync(domainEventBatch.Events, savedChanges)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

        return savedChanges;
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        DetectChangesIfNeeded();

        var domainEventBatch = CollectDomainEventBatch();
        var savedChanges = await SaveChangesWithIntegrationEventsAsync(
                domainEventBatch.Events,
                acceptAllChangesOnSuccess,
                ct => base.SaveChangesAsync(acceptAllChangesOnSuccess, ct),
                ct => base.SaveChangesAsync(acceptAllChangesOnSuccess: false, ct),
                cancellationToken)
            .ConfigureAwait(false);

        ClearDomainEventsAfterSuccessfulSave(domainEventBatch.Entities, savedChanges);

        await DispatchLocalDomainEventsAfterSaveAsync(domainEventBatch.Events, savedChanges).ConfigureAwait(false);

        return savedChanges;
    }

    private void DetectChangesIfNeeded()
    {
        if (!ChangeTracker.AutoDetectChangesEnabled)
        {
            ChangeTracker.DetectChanges();
        }
    }

    private DomainEventBatch CollectDomainEventBatch()
    {
        var domainEntities = ChangeTracker
            .Entries<Entity>()
            .Where(entry => entry.Entity.DomainEvents?.Any() == true)
            .Select(entry => entry.Entity)
            .ToList();

        var domainEvents = domainEntities
            .SelectMany(entity => entity.DomainEvents!)
            .ToList();

        return new DomainEventBatch(domainEntities, domainEvents);
    }

    private int SaveChangesWithIntegrationEvents(
        IReadOnlyCollection<Event> domainEvents,
        bool acceptAllChangesOnSuccess,
        Func<int> saveChanges,
        Func<int> saveChangesWithoutAccepting)
    {
        if (!domainEvents.Any(IsIntegrationEvent))
        {
            return saveChanges();
        }

        if (_integrationEventPublisher is not ITransactionalIntegrationEventPublisher transactionalPublisher)
        {
            throw new InvalidOperationException(
                "A transactional integration event publisher is required when aggregate changes contain integration events.");
        }

        var savedChanges = transactionalPublisher.SaveChangesAndPublish(
            this,
            domainEvents,
            saveChangesWithoutAccepting);

        AcceptAllChangesAfterSuccessfulTransaction(savedChanges, acceptAllChangesOnSuccess);

        return savedChanges;
    }

    private async Task<int> SaveChangesWithIntegrationEventsAsync(
        IReadOnlyCollection<Event> domainEvents,
        bool acceptAllChangesOnSuccess,
        Func<CancellationToken, Task<int>> saveChangesAsync,
        Func<CancellationToken, Task<int>> saveChangesWithoutAcceptingAsync,
        CancellationToken cancellationToken)
    {
        if (!domainEvents.Any(IsIntegrationEvent))
        {
            return await saveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_integrationEventPublisher is not ITransactionalIntegrationEventPublisher transactionalPublisher)
        {
            throw new InvalidOperationException(
                "A transactional integration event publisher is required when aggregate changes contain integration events.");
        }

        var savedChanges = await transactionalPublisher
            .SaveChangesAndPublishAsync(this, domainEvents, saveChangesWithoutAcceptingAsync, cancellationToken)
            .ConfigureAwait(false);

        AcceptAllChangesAfterSuccessfulTransaction(savedChanges, acceptAllChangesOnSuccess);

        return savedChanges;
    }

    private static bool IsIntegrationEvent(Event domainEvent)
    {
        return domainEvent is IIntegrationEvent
            || Attribute.IsDefined(domainEvent.GetType(), typeof(IntegrationEventAttribute));
    }

    private static void ClearDomainEventsAfterSuccessfulSave(
        IReadOnlyCollection<Entity> domainEntities,
        int savedChanges)
    {
        if (savedChanges <= 0)
        {
            return;
        }

        foreach (var entity in domainEntities)
        {
            entity.ClearDomainEvents();
        }
    }

    private void AcceptAllChangesAfterSuccessfulTransaction(
        int savedChanges,
        bool acceptAllChangesOnSuccess)
    {
        if (savedChanges > 0 && acceptAllChangesOnSuccess)
        {
            ChangeTracker.AcceptAllChanges();
        }
    }

    private async Task DispatchLocalDomainEventsAfterSaveAsync(
        IReadOnlyCollection<Event> domainEvents,
        int savedChanges)
    {
        if (savedChanges <= 0 || domainEvents.Count == 0)
        {
            return;
        }

        var correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString();

        var localDomainEvents = domainEvents.OfType<DomainEvent>().ToList();
        if (localDomainEvents.Count == 0)
        {
            return;
        }

        _logger.LogDebug(
            "Dispatching {Count} local domain events. CorrelationId: {CorrelationId}",
            localDomainEvents.Count,
            correlationId);

        try
        {
            await _domainEventDispatcher.DispatchAsync(localDomainEvents).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Local domain event dispatch failed. CorrelationId: {CorrelationId}", correlationId);
        }
    }

    private sealed record DomainEventBatch(
        IReadOnlyCollection<Entity> Entities,
        IReadOnlyCollection<Event> Events);
}
