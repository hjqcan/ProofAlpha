using Autotrade.SelfImprove.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;
using NetDevPack.Messaging;

namespace Autotrade.SelfImprove.Infra.Data.Design;

public sealed class SelfImproveContextFactory : IDesignTimeDbContextFactory<SelfImproveContext>
{
    public SelfImproveContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("AUTOTRADE_DATABASE")
            ?? "Host=localhost;Port=5432;Database=autotrade;Username=postgres;Password=123456";

        var builder = new DbContextOptionsBuilder<SelfImproveContext>();
        builder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable(SelfImproveContext.MigrationsHistoryTable));

        return new SelfImproveContext(
            builder.Options,
            new NoopDomainEventDispatcher(),
            NullLogger<SelfImproveContext>.Instance);
    }

    private sealed class NoopDomainEventDispatcher : IDomainEventDispatcher
    {
        public void Dispatch(IEnumerable<DomainEvent> domainEvents) { }

        public Task DispatchAsync(IEnumerable<DomainEvent> domainEvents) => Task.CompletedTask;
    }
}
