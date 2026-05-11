using Autotrade.Trading.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;
using NetDevPack.Messaging;

namespace Autotrade.Trading.Infra.Data.Design;

/// <summary>
/// 设计时 DbContext 工厂（用于生成迁移）。
/// 风格对齐 third-party/grukirbs：优先从环境变量取连接串，并提供本地默认值。
/// </summary>
public sealed class TradingContextFactory : IDesignTimeDbContextFactory<TradingContext>
{
    public TradingContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("AUTOTRADE_DATABASE")
            ?? "Host=localhost;Port=5432;Database=autotrade;Username=postgres;Password=123456";

        var builder = new DbContextOptionsBuilder<TradingContext>();
        builder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable(TradingContext.MigrationsHistoryTable));

        return new TradingContext(
            builder.Options,
            new NullDomainEventDispatcher(),
            NullLogger<TradingContext>.Instance);
    }

    private sealed class NullDomainEventDispatcher : IDomainEventDispatcher
    {
        public void Dispatch(IEnumerable<DomainEvent> domainEvents) { }

        public Task DispatchAsync(IEnumerable<DomainEvent> domainEvents) => Task.CompletedTask;
    }
}

