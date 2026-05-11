using Autotrade.Domain.Abstractions.EventBus;
using Autotrade.Infra.Data.Core.Context;
using Autotrade.Trading.Domain.Entities;
using Autotrade.Trading.Domain.Shared.Enums;
using Autotrade.Trading.Domain.Shared.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Logging;
using NetDevPack.Messaging;

namespace Autotrade.Trading.Infra.Data.Context;

/// <summary>
/// Trading 限界上下文的 DbContext。
/// </summary>
public sealed class TradingContext : BaseDbContext
{
    public const string MigrationsHistoryTable = "__EFMigrationsHistory_Trading";

    public TradingContext(
        DbContextOptions<TradingContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        ILogger<TradingContext> logger,
        IIntegrationEventPublisher? integrationEventPublisher = null)
        : base(options, domainEventDispatcher, integrationEventPublisher, logger)
    {
        ChangeTracker.AutoDetectChangesEnabled = false;
    }

    public DbSet<TradingAccount> TradingAccounts => Set<TradingAccount>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Position> Positions => Set<Position>();

    public DbSet<RiskEvent> RiskEvents => Set<RiskEvent>();

    public DbSet<RiskEventLog> RiskEventLogs => Set<RiskEventLog>();

    public DbSet<OrderEvent> OrderEvents => Set<OrderEvent>();

    public DbSet<Trade> Trades => Set<Trade>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // DomainEvent 不落库
        modelBuilder.Ignore<Event>();

        modelBuilder.Entity<TradingAccount>(ConfigureTradingAccount);
        modelBuilder.Entity<Order>(ConfigureOrder);
        modelBuilder.Entity<Position>(ConfigurePosition);
        modelBuilder.Entity<RiskEvent>(ConfigureRiskEvent);
        modelBuilder.Entity<RiskEventLog>(ConfigureRiskEventLog);
        modelBuilder.Entity<OrderEvent>(ConfigureOrderEvent);
        modelBuilder.Entity<Trade>(ConfigureTrade);

        base.OnModelCreating(modelBuilder);
    }

    private static void ConfigureTradingAccount(EntityTypeBuilder<TradingAccount> entity)
    {
        entity.ToTable("TradingAccounts");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.CreatedAtUtc).IsRequired();
        entity.Property(x => x.UpdatedAtUtc).IsRequired();

        entity.Property(x => x.WalletAddress)
            .HasColumnName("WalletAddress")
            .HasMaxLength(128)
            .IsRequired();

        // 账户 key（WalletAddress）语义上应唯一（Live = 真实钱包地址；Paper = 固定 key，例如 "paper"）
        entity.HasIndex(x => x.WalletAddress)
            .IsUnique()
            .HasDatabaseName("UX_TradingAccounts_WalletAddress");

        entity.Property(x => x.TotalCapital)
            .HasColumnName("TotalCapital")
            .HasColumnType("numeric(18,6)")
            .IsRequired();

        entity.Property(x => x.AvailableCapital)
            .HasColumnName("AvailableCapital")
            .HasColumnType("numeric(18,6)")
            .IsRequired();

        entity.Property(x => x.AccountUpdatedAtUtc)
            .HasColumnName("AccountUpdatedAtUtc")
            .IsRequired();

        entity.HasMany(x => x.Orders)
            .WithOne()
            .HasForeignKey(o => o.TradingAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasMany(x => x.Positions)
            .WithOne()
            .HasForeignKey(p => p.TradingAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasMany(x => x.RiskEvents)
            .WithOne()
            .HasForeignKey(r => r.TradingAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasMany(x => x.Trades)
            .WithOne()
            .HasForeignKey(t => t.TradingAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        // 乐观并发控制 - 仅 PostgreSQL 支持（SQLite 测试会跳过）
        entity.Property(x => x.RowVersion)
            .HasMaxLength(8)
            .IsConcurrencyToken();

        entity.HasIndex(x => x.Id).HasDatabaseName("IX_TradingAccounts_Id");
    }

    private static void ConfigureOrder(EntityTypeBuilder<Order> entity)
    {
        entity.ToTable("Orders");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.TradingAccountId).IsRequired();

        entity.Property(x => x.MarketId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(x => x.Outcome)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        entity.Property(x => x.Side)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        entity.Property(x => x.OrderType)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        entity.Property(x => x.TimeInForce)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        entity.Property(x => x.GoodTilDateUtc);

        entity.Property(x => x.NegRisk)
            .IsRequired()
            .HasDefaultValue(false);

        entity.OwnsOne(
            x => x.Price,
            price =>
            {
                price.WithOwner();
                price.Property(p => p.Value)
                    .HasColumnName("Price")
                    .HasColumnType("numeric(18,10)")
                    .IsRequired();
            });

        entity.OwnsOne(
            x => x.Quantity,
            quantity =>
            {
                quantity.WithOwner();
                quantity.Property(q => q.Value)
                    .HasColumnName("Quantity")
                    .HasColumnType("numeric(18,10)")
                    .IsRequired();
            });

        entity.OwnsOne(
            x => x.FilledQuantity,
            filled =>
            {
                filled.WithOwner();
                filled.Property(q => q.Value)
                    .HasColumnName("FilledQuantity")
                    .HasColumnType("numeric(18,10)")
                    .IsRequired();
            });

        entity.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        entity.Property(x => x.RejectionReason)
            .HasMaxLength(512);

        entity.Property(x => x.CreatedAtUtc).IsRequired();
        entity.Property(x => x.UpdatedAtUtc).IsRequired();

        entity.Property(x => x.StrategyId)
            .HasMaxLength(128);

        entity.Property(x => x.ClientOrderId)
            .HasMaxLength(256);

        entity.Property(x => x.ExchangeOrderId)
            .HasMaxLength(256);

        entity.Property(x => x.OrderSalt)
            .HasMaxLength(128);

        entity.Property(x => x.OrderTimestamp)
            .HasMaxLength(32);

        entity.Property(x => x.TokenId)
            .HasMaxLength(256);

        entity.Property(x => x.CorrelationId)
            .HasMaxLength(256);

        // 乐观并发控制 - 仅 PostgreSQL 支持（SQLite 测试会跳过）
        entity.Property(x => x.RowVersion)
            .HasMaxLength(8)
            .IsConcurrencyToken();

        entity.HasIndex(x => new { x.TradingAccountId, x.Status, x.CreatedAtUtc })
            .HasDatabaseName("IX_Orders_Aggregate_Status_Time");

        // Task 8 要求的索引
        entity.HasIndex(x => x.MarketId)
            .HasDatabaseName("IX_Orders_MarketId");

        entity.HasIndex(x => x.Status)
            .HasDatabaseName("IX_Orders_Status");

        entity.HasIndex(x => x.StrategyId)
            .HasDatabaseName("IX_Orders_StrategyId");

        entity.HasIndex(x => x.ClientOrderId)
            .HasDatabaseName("IX_Orders_ClientOrderId");

        entity.HasIndex(x => x.ExchangeOrderId)
            .IsUnique()
            .HasFilter("\"ExchangeOrderId\" IS NOT NULL")
            .HasDatabaseName("UX_Orders_ExchangeOrderId");

        entity.HasIndex(x => x.CorrelationId)
            .HasDatabaseName("IX_Orders_CorrelationId");

        entity.HasIndex(x => x.CreatedAtUtc)
            .HasDatabaseName("IX_Orders_CreatedAtUtc");
    }

    private static void ConfigurePosition(EntityTypeBuilder<Position> entity)
    {
        entity.ToTable("Positions");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.TradingAccountId).IsRequired();

        entity.Property(x => x.MarketId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(x => x.Outcome)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        entity.OwnsOne(
            x => x.Quantity,
            quantity =>
            {
                quantity.WithOwner();
                quantity.Property(q => q.Value)
                    .HasColumnName("Quantity")
                    .HasColumnType("numeric(18,10)")
                    .IsRequired();
            });

        entity.OwnsOne(
            x => x.AverageCost,
            avg =>
            {
                avg.WithOwner();
                avg.Property(p => p.Value)
                    .HasColumnName("AverageCost")
                    .HasColumnType("numeric(18,10)")
                    .IsRequired();
            });

        entity.Property(x => x.RealizedPnl)
            .HasColumnType("numeric(18,10)")
            .IsRequired();

        entity.Property(x => x.UpdatedAtUtc).IsRequired();

        // 同一账户在同一市场同一结果侧只能有一条持仓记录（并发下可防止重复插入）。
        entity.HasIndex(x => new { x.TradingAccountId, x.MarketId, x.Outcome })
            .IsUnique()
            .HasDatabaseName("UX_Positions_Aggregate_Market_Outcome");
    }

    private static void ConfigureRiskEvent(EntityTypeBuilder<RiskEvent> entity)
    {
        entity.ToTable("RiskEvents");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.TradingAccountId).IsRequired();

        entity.Property(x => x.Code)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(x => x.Severity)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        entity.Property(x => x.Message)
            .HasMaxLength(2048)
            .IsRequired();

        entity.Property(x => x.ContextJson)
            .HasColumnType("jsonb")
            .IsRequired();

        entity.Property(x => x.CreatedAtUtc).IsRequired();

        entity.HasIndex(x => new { x.TradingAccountId, x.Severity, x.CreatedAtUtc })
            .HasDatabaseName("IX_RiskEvents_Aggregate_Severity_Time");
    }

    private static void ConfigureRiskEventLog(EntityTypeBuilder<RiskEventLog> entity)
    {
        entity.ToTable("RiskEventLogs");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.Code)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(x => x.Severity)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        entity.Property(x => x.Message)
            .HasMaxLength(2048)
            .IsRequired();

        entity.Property(x => x.StrategyId)
            .HasMaxLength(128);

        entity.Property(x => x.MarketId)
            .HasMaxLength(128);

        entity.Property(x => x.ContextJson)
            .HasColumnType("jsonb")
            .IsRequired();

        entity.Property(x => x.CreatedAtUtc).IsRequired();

        entity.HasIndex(x => new { x.Severity, x.CreatedAtUtc })
            .HasDatabaseName("IX_RiskEventLogs_Severity_Time");

        entity.HasIndex(x => new { x.StrategyId, x.CreatedAtUtc })
            .HasDatabaseName("IX_RiskEventLogs_Strategy_Time");

        entity.HasIndex(x => new { x.Code, x.CreatedAtUtc })
            .HasDatabaseName("IX_RiskEventLogs_Code_Time");
    }

    private static void ConfigureOrderEvent(EntityTypeBuilder<OrderEvent> entity)
    {
        entity.ToTable("OrderEvents");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.OrderId).IsRequired();

        entity.Property(x => x.ClientOrderId)
            .HasMaxLength(256)
            .IsRequired();

        entity.Property(x => x.StrategyId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(x => x.MarketId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(x => x.EventType)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        entity.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        entity.Property(x => x.Message)
            .HasMaxLength(1024)
            .IsRequired();

        entity.Property(x => x.ContextJson)
            .HasColumnType("jsonb");

        entity.Property(x => x.CorrelationId)
            .HasMaxLength(256);

        entity.Property(x => x.RunSessionId);

        entity.Property(x => x.CreatedAtUtc).IsRequired();

        // 注：OrderId 不设 FK 约束，因为审计日志可能在 Order 实体创建之前就需要记录
        // 仅通过索引支持查询关联

        // 索引
        entity.HasIndex(x => x.OrderId)
            .HasDatabaseName("IX_OrderEvents_OrderId");

        entity.HasIndex(x => x.ClientOrderId)
            .HasDatabaseName("IX_OrderEvents_ClientOrderId");

        entity.HasIndex(x => x.StrategyId)
            .HasDatabaseName("IX_OrderEvents_StrategyId");

        entity.HasIndex(x => x.MarketId)
            .HasDatabaseName("IX_OrderEvents_MarketId");

        entity.HasIndex(x => x.CorrelationId)
            .HasDatabaseName("IX_OrderEvents_CorrelationId");

        entity.HasIndex(x => x.RunSessionId)
            .HasDatabaseName("IX_OrderEvents_RunSessionId");

        entity.HasIndex(x => x.CreatedAtUtc)
            .HasDatabaseName("IX_OrderEvents_CreatedAtUtc");

        entity.HasIndex(x => new { x.OrderId, x.CreatedAtUtc })
            .HasDatabaseName("IX_OrderEvents_Order_Time");
    }

    private static void ConfigureTrade(EntityTypeBuilder<Trade> entity)
    {
        entity.ToTable("Trades");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.OrderId).IsRequired();
        entity.Property(x => x.TradingAccountId).IsRequired();

        entity.Property(x => x.ClientOrderId)
            .HasMaxLength(256)
            .IsRequired();

        entity.Property(x => x.StrategyId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(x => x.MarketId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(x => x.TokenId)
            .HasMaxLength(256)
            .IsRequired();

        entity.Property(x => x.Outcome)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        entity.Property(x => x.Side)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        entity.OwnsOne(
            x => x.Price,
            price =>
            {
                price.WithOwner();
                price.Property(p => p.Value)
                    .HasColumnName("Price")
                    .HasColumnType("numeric(18,10)")
                    .IsRequired();
            });

        entity.OwnsOne(
            x => x.Quantity,
            quantity =>
            {
                quantity.WithOwner();
                quantity.Property(q => q.Value)
                    .HasColumnName("Quantity")
                    .HasColumnType("numeric(18,10)")
                    .IsRequired();
            });

        entity.Property(x => x.ExchangeTradeId)
            .HasMaxLength(256)
            .IsRequired();

        entity.Property(x => x.Fee)
            .HasColumnType("numeric(18,10)")
            .IsRequired();

        entity.Property(x => x.CorrelationId)
            .HasMaxLength(256);

        entity.Property(x => x.CreatedAtUtc).IsRequired();

        // 注：OrderId 不设独立 FK，Trade 通过 TradingAccountId FK 保证关联
        // OrderId 用于审计关联查询

        // 索引
        entity.HasIndex(x => x.OrderId)
            .HasDatabaseName("IX_Trades_OrderId");

        entity.HasIndex(x => x.TradingAccountId)
            .HasDatabaseName("IX_Trades_TradingAccountId");

        entity.HasIndex(x => x.ClientOrderId)
            .HasDatabaseName("IX_Trades_ClientOrderId");

        entity.HasIndex(x => x.ExchangeTradeId)
            .IsUnique()
            .HasDatabaseName("UX_Trades_ExchangeTradeId");

        entity.HasIndex(x => x.StrategyId)
            .HasDatabaseName("IX_Trades_StrategyId");

        entity.HasIndex(x => x.MarketId)
            .HasDatabaseName("IX_Trades_MarketId");

        entity.HasIndex(x => x.CorrelationId)
            .HasDatabaseName("IX_Trades_CorrelationId");

        entity.HasIndex(x => x.CreatedAtUtc)
            .HasDatabaseName("IX_Trades_CreatedAtUtc");

        entity.HasIndex(x => new { x.StrategyId, x.CreatedAtUtc })
            .HasDatabaseName("IX_Trades_Strategy_Time");

        entity.HasIndex(x => new { x.MarketId, x.CreatedAtUtc })
            .HasDatabaseName("IX_Trades_Market_Time");
    }
}

