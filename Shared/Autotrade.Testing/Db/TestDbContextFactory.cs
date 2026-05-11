using Autotrade.Domain.Abstractions.EventBus;
using Autotrade.Infra.Data.Core.EventBus;
using Autotrade.MarketData.Infra.Data.Context;
using Autotrade.Trading.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NetDevPack.Messaging;

namespace Autotrade.Testing.Db;

/// <summary>
/// 测试用 DbContext 工厂：提供 SQLite in-memory（默认）和可选 Postgres 构造方法。
/// </summary>
public static class TestDbContextFactory
{
    public static TradingContext CreateTradingContextSqlite(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        IIntegrationEventPublisher? integrationEventPublisher = null)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var options = new DbContextOptionsBuilder<TradingContext>()
            .UseSqlite(connection)
            .EnableDetailedErrors()
            .Options;

        return new TradingContext(
            options,
            domainEventDispatcher: new NullDomainEventDispatcher(),
            logger: NullLogger<TradingContext>.Instance,
            integrationEventPublisher: integrationEventPublisher ?? NullTransactionalIntegrationEventPublisher.Instance);
    }

    public static MarketDataContext CreateMarketDataContextSqlite(Microsoft.Data.Sqlite.SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var options = new DbContextOptionsBuilder<MarketDataContext>()
            .UseSqlite(connection)
            .EnableDetailedErrors()
            .Options;

        return new MarketDataContext(
            options,
            domainEventDispatcher: new NullDomainEventDispatcher(),
            logger: NullLogger<MarketDataContext>.Instance,
            integrationEventPublisher: NullTransactionalIntegrationEventPublisher.Instance);
    }

    public static TradingContext CreateTradingContextPostgres(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Postgres connectionString 不能为空", nameof(connectionString));
        }

        var options = new DbContextOptionsBuilder<TradingContext>()
            .UseNpgsql(connectionString)
            .EnableDetailedErrors()
            .Options;

        return new TradingContext(
            options,
            domainEventDispatcher: new NullDomainEventDispatcher(),
            logger: NullLogger<TradingContext>.Instance,
            integrationEventPublisher: NullTransactionalIntegrationEventPublisher.Instance);
    }

    public static MarketDataContext CreateMarketDataContextPostgres(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Postgres connectionString 不能为空", nameof(connectionString));
        }

        var options = new DbContextOptionsBuilder<MarketDataContext>()
            .UseNpgsql(connectionString)
            .EnableDetailedErrors()
            .Options;

        return new MarketDataContext(
            options,
            domainEventDispatcher: new NullDomainEventDispatcher(),
            logger: NullLogger<MarketDataContext>.Instance,
            integrationEventPublisher: NullTransactionalIntegrationEventPublisher.Instance);
    }

    private sealed class NullTransactionalIntegrationEventPublisher : ITransactionalIntegrationEventPublisher
    {
        public static readonly NullTransactionalIntegrationEventPublisher Instance = new();

        public Task PublishAsync(IEnumerable<Event> domainEvents, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public int SaveChangesAndPublish(
            DbContext dbContext,
            IReadOnlyCollection<Event> domainEvents,
            Func<int> saveChanges)
        {
            return saveChanges();
        }

        public Task<int> SaveChangesAndPublishAsync(
            DbContext dbContext,
            IReadOnlyCollection<Event> domainEvents,
            Func<CancellationToken, Task<int>> saveChangesAsync,
            CancellationToken cancellationToken = default)
        {
            return saveChangesAsync(cancellationToken);
        }
    }
}

