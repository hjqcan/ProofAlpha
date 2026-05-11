using Autotrade.OpportunityDiscovery.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;
using NetDevPack.Messaging;

namespace Autotrade.OpportunityDiscovery.Infra.Data.Design;

public sealed class OpportunityDiscoveryContextFactory : IDesignTimeDbContextFactory<OpportunityDiscoveryContext>
{
    public OpportunityDiscoveryContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("AUTOTRADE_DATABASE")
            ?? "Host=localhost;Port=5432;Database=autotrade;Username=postgres;Password=123456";

        var builder = new DbContextOptionsBuilder<OpportunityDiscoveryContext>();
        builder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable(OpportunityDiscoveryContext.MigrationsHistoryTable));

        return new OpportunityDiscoveryContext(
            builder.Options,
            new NoopDomainEventDispatcher(),
            NullLogger<OpportunityDiscoveryContext>.Instance);
    }

    private sealed class NoopDomainEventDispatcher : IDomainEventDispatcher
    {
        public void Dispatch(IEnumerable<DomainEvent> domainEvents) { }

        public Task DispatchAsync(IEnumerable<DomainEvent> domainEvents) => Task.CompletedTask;
    }
}
