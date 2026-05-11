using Autotrade.Domain.Abstractions.EventBus;
using Autotrade.Infra.Data.Core.Context;
using Autotrade.MarketData.Domain.Entities;
using Autotrade.MarketData.Domain.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Logging;
using NetDevPack.Messaging;

namespace Autotrade.MarketData.Infra.Data.Context;

/// <summary>
/// MarketData 限界上下文的 DbContext。
/// </summary>
public sealed class MarketDataContext : BaseDbContext
{
    public const string MigrationsHistoryTable = "__EFMigrationsHistory_MarketData";

    public MarketDataContext(
        DbContextOptions<MarketDataContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        ILogger<MarketDataContext> logger,
        IIntegrationEventPublisher? integrationEventPublisher = null)
        : base(options, domainEventDispatcher, integrationEventPublisher, logger)
    {
        ChangeTracker.AutoDetectChangesEnabled = false;
    }

    public DbSet<Market> Markets => Set<Market>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // DomainEvent 不落库
        modelBuilder.Ignore<Event>();

        modelBuilder.Entity<Market>(ConfigureMarket);

        base.OnModelCreating(modelBuilder);
    }

    private static void ConfigureMarket(EntityTypeBuilder<Market> entity)
    {
        entity.ToTable("Markets");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.MarketId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(x => x.Name)
            .HasMaxLength(256)
            .IsRequired();

        entity.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        entity.Property(x => x.ExpiresAtUtc);

        entity.Property(x => x.CreatedAtUtc).IsRequired();
        entity.Property(x => x.UpdatedAtUtc).IsRequired();

        entity.HasIndex(x => x.MarketId)
            .IsUnique()
            .HasDatabaseName("IX_Markets_MarketId_Unique");
    }
}

