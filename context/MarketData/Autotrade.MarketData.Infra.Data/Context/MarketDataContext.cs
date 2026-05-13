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

    public DbSet<MarketPriceTick> MarketPriceTicks => Set<MarketPriceTick>();

    public DbSet<OrderBookTopTick> OrderBookTopTicks => Set<OrderBookTopTick>();

    public DbSet<OrderBookDepthSnapshot> OrderBookDepthSnapshots => Set<OrderBookDepthSnapshot>();

    public DbSet<ClobTradeTick> ClobTradeTicks => Set<ClobTradeTick>();

    public DbSet<MarketResolutionEvent> MarketResolutionEvents => Set<MarketResolutionEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // DomainEvent 不落库
        modelBuilder.Ignore<Event>();

        modelBuilder.Entity<Market>(ConfigureMarket);
        modelBuilder.Entity<MarketPriceTick>(ConfigureMarketPriceTick);
        modelBuilder.Entity<OrderBookTopTick>(ConfigureOrderBookTopTick);
        modelBuilder.Entity<OrderBookDepthSnapshot>(ConfigureOrderBookDepthSnapshot);
        modelBuilder.Entity<ClobTradeTick>(ConfigureClobTradeTick);
        modelBuilder.Entity<MarketResolutionEvent>(ConfigureMarketResolutionEvent);

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

    private static void ConfigureMarketPriceTick(EntityTypeBuilder<MarketPriceTick> entity)
    {
        entity.ToTable("MarketPriceTicks");
        entity.HasKey(x => x.Id);
        ConfigureMarketTokenTime(entity);
        entity.Property(x => x.Price).HasPrecision(18, 8).IsRequired();
        entity.Property(x => x.Size).HasPrecision(28, 8);
        ConfigureSourcePayload(entity);
        entity.HasIndex(x => new { x.MarketId, x.TokenId, x.TimestampUtc })
            .HasDatabaseName("IX_MarketPriceTicks_Market_Token_Time");
        entity.HasIndex(x => new { x.MarketId, x.TokenId, x.TimestampUnixMilliseconds })
            .HasDatabaseName("IX_MarketPriceTicks_Market_Token_UnixTime");
        entity.HasIndex(x => new { x.MarketId, x.TokenId, x.SourceName, x.SourceSequence })
            .HasDatabaseName("IX_MarketPriceTicks_Market_Token_Source_Sequence");
    }

    private static void ConfigureOrderBookTopTick(EntityTypeBuilder<OrderBookTopTick> entity)
    {
        entity.ToTable("OrderBookTopTicks");
        entity.HasKey(x => x.Id);
        ConfigureMarketTokenTime(entity);
        entity.Property(x => x.BestBidPrice).HasPrecision(18, 8);
        entity.Property(x => x.BestBidSize).HasPrecision(28, 8);
        entity.Property(x => x.BestAskPrice).HasPrecision(18, 8);
        entity.Property(x => x.BestAskSize).HasPrecision(28, 8);
        entity.Property(x => x.Spread).HasPrecision(18, 8);
        ConfigureSourcePayload(entity);
        entity.HasIndex(x => new { x.MarketId, x.TokenId, x.TimestampUtc })
            .HasDatabaseName("IX_OrderBookTopTicks_Market_Token_Time");
        entity.HasIndex(x => new { x.MarketId, x.TokenId, x.TimestampUnixMilliseconds })
            .HasDatabaseName("IX_OrderBookTopTicks_Market_Token_UnixTime");
        entity.HasIndex(x => new { x.MarketId, x.TokenId, x.SourceName, x.SourceSequence })
            .HasDatabaseName("IX_OrderBookTopTicks_Market_Token_Source_Sequence");
    }

    private static void ConfigureOrderBookDepthSnapshot(EntityTypeBuilder<OrderBookDepthSnapshot> entity)
    {
        entity.ToTable("OrderBookDepthSnapshots");
        entity.HasKey(x => x.Id);
        ConfigureMarketTokenTime(entity);
        entity.Property(x => x.SnapshotHash).HasMaxLength(256).IsRequired();
        entity.Property(x => x.BidsJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.AsksJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.SourceName).HasMaxLength(128).IsRequired();
        entity.Property(x => x.RawJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.CreatedAtUtc).IsRequired();
        entity.HasIndex(x => new { x.MarketId, x.TokenId, x.TimestampUtc })
            .HasDatabaseName("IX_OrderBookDepthSnapshots_Market_Token_Time");
        entity.HasIndex(x => new { x.MarketId, x.TokenId, x.TimestampUnixMilliseconds })
            .HasDatabaseName("IX_OrderBookDepthSnapshots_Market_Token_UnixTime");
        entity.HasIndex(x => new { x.MarketId, x.TokenId, x.SnapshotHash, x.TimestampUtc })
            .IsUnique()
            .HasDatabaseName("IX_OrderBookDepthSnapshots_Dedup");
    }

    private static void ConfigureClobTradeTick(EntityTypeBuilder<ClobTradeTick> entity)
    {
        entity.ToTable("ClobTradeTicks");
        entity.HasKey(x => x.Id);
        ConfigureMarketTokenTime(entity);
        entity.Property(x => x.ExchangeTradeId).HasMaxLength(256).IsRequired();
        entity.Property(x => x.Price).HasPrecision(18, 8).IsRequired();
        entity.Property(x => x.Size).HasPrecision(28, 8).IsRequired();
        entity.Property(x => x.Side).HasMaxLength(16).IsRequired();
        entity.Property(x => x.FeeRateBps).HasPrecision(18, 8);
        entity.Property(x => x.SourceName).HasMaxLength(128).IsRequired();
        entity.Property(x => x.RawJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.CreatedAtUtc).IsRequired();
        entity.HasIndex(x => new { x.MarketId, x.TokenId, x.TimestampUtc })
            .HasDatabaseName("IX_ClobTradeTicks_Market_Token_Time");
        entity.HasIndex(x => new { x.MarketId, x.TokenId, x.TimestampUnixMilliseconds })
            .HasDatabaseName("IX_ClobTradeTicks_Market_Token_UnixTime");
        entity.HasIndex(x => new { x.MarketId, x.TokenId, x.ExchangeTradeId })
            .IsUnique()
            .HasDatabaseName("IX_ClobTradeTicks_Dedup");
    }

    private static void ConfigureMarketResolutionEvent(EntityTypeBuilder<MarketResolutionEvent> entity)
    {
        entity.ToTable("MarketResolutionEvents");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.MarketId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.ResolvedAtUtc).IsRequired();
        entity.Property(x => x.ResolvedUnixMilliseconds).IsRequired();
        entity.Property(x => x.Outcome).HasMaxLength(128).IsRequired();
        entity.Property(x => x.SourceName).HasMaxLength(128).IsRequired();
        entity.Property(x => x.RawJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.CreatedAtUtc).IsRequired();
        entity.HasIndex(x => new { x.MarketId, x.ResolvedAtUtc })
            .HasDatabaseName("IX_MarketResolutionEvents_Market_ResolvedAt");
        entity.HasIndex(x => new { x.MarketId, x.ResolvedUnixMilliseconds })
            .HasDatabaseName("IX_MarketResolutionEvents_Market_ResolvedUnix");
    }

    private static void ConfigureMarketTokenTime<TEntity>(EntityTypeBuilder<TEntity> entity)
        where TEntity : class
    {
        entity.Property<string>("MarketId").HasMaxLength(128).IsRequired();
        entity.Property<string>("TokenId").HasMaxLength(256).IsRequired();
        entity.Property<DateTimeOffset>("TimestampUtc").IsRequired();
        entity.Property<long>("TimestampUnixMilliseconds").IsRequired();
    }

    private static void ConfigureSourcePayload<TEntity>(EntityTypeBuilder<TEntity> entity)
        where TEntity : class
    {
        entity.Property<string>("SourceName").HasMaxLength(128).IsRequired();
        entity.Property<string?>("SourceSequence").HasMaxLength(256);
        entity.Property<string>("RawJson").HasColumnType("jsonb").IsRequired();
        entity.Property<DateTimeOffset>("CreatedAtUtc").IsRequired();
    }
}
