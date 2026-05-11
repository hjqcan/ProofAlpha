using Autotrade.MarketData.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;
using NetDevPack.Messaging;

namespace Autotrade.MarketData.Infra.Data.Design;

/// <summary>
/// 设计时 DbContext 工厂（用于生成迁移）。
/// </summary>
public sealed class MarketDataContextFactory : IDesignTimeDbContextFactory<MarketDataContext>
{
    public MarketDataContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("AUTOTRADE_DATABASE")
            ?? "Host=localhost;Port=5432;Database=autotrade;Username=postgres;Password=123456";

        var builder = new DbContextOptionsBuilder<MarketDataContext>();
        builder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable(MarketDataContext.MigrationsHistoryTable));

        return new MarketDataContext(
            builder.Options,
            new NullDomainEventDispatcher(),
            NullLogger<MarketDataContext>.Instance);
    }

    private sealed class NullDomainEventDispatcher : IDomainEventDispatcher
    {
        public void Dispatch(IEnumerable<DomainEvent> domainEvents) { }

        public Task DispatchAsync(IEnumerable<DomainEvent> domainEvents) => Task.CompletedTask;
    }
}

