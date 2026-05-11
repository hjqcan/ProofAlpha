using Autotrade.Strategy.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;
using NetDevPack.Messaging;

namespace Autotrade.Strategy.Infra.Data.Design;

public sealed class StrategyContextFactory : IDesignTimeDbContextFactory<StrategyContext>
{
    public StrategyContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("AUTOTRADE_DATABASE")
            ?? "Host=localhost;Port=5432;Database=autotrade;Username=postgres;Password=123456";

        var builder = new DbContextOptionsBuilder<StrategyContext>();
        builder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable(StrategyContext.MigrationsHistoryTable));

        return new StrategyContext(
            builder.Options,
            new NullDomainEventDispatcher(),
            NullLogger<StrategyContext>.Instance);
    }

    private sealed class NullDomainEventDispatcher : IDomainEventDispatcher
    {
        public void Dispatch(IEnumerable<DomainEvent> domainEvents) { }

        public Task DispatchAsync(IEnumerable<DomainEvent> domainEvents) => Task.CompletedTask;
    }
}
