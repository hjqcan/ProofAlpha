using Autotrade.Domain.Abstractions.EventBus;
using Autotrade.Infra.Data.Core.Context;
using Autotrade.Strategy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Logging;
using NetDevPack.Messaging;

namespace Autotrade.Strategy.Infra.Data.Context;

public sealed class StrategyContext : BaseDbContext
{
    public const string MigrationsHistoryTable = "__EFMigrationsHistory_Strategy";

    public StrategyContext(
        DbContextOptions<StrategyContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        ILogger<StrategyContext> logger,
        IIntegrationEventPublisher? integrationEventPublisher = null)
        : base(options, domainEventDispatcher, integrationEventPublisher, logger)
    {
        ChangeTracker.AutoDetectChangesEnabled = false;
    }

    public DbSet<StrategyDecisionLog> StrategyDecisionLogs => Set<StrategyDecisionLog>();

    public DbSet<StrategyObservationLog> StrategyObservationLogs => Set<StrategyObservationLog>();

    public DbSet<PaperRunSession> PaperRunSessions => Set<PaperRunSession>();

    public DbSet<StrategyRunState> StrategyRunStates => Set<StrategyRunState>();

    public DbSet<StrategyParameterVersion> StrategyParameterVersions => Set<StrategyParameterVersion>();

    public DbSet<CommandAuditLog> CommandAuditLogs => Set<CommandAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Ignore<Event>();

        modelBuilder.Entity<StrategyDecisionLog>(ConfigureDecisionLog);
        modelBuilder.Entity<StrategyObservationLog>(ConfigureObservationLog);
        modelBuilder.Entity<PaperRunSession>(ConfigurePaperRunSession);
        modelBuilder.Entity<StrategyRunState>(ConfigureRunState);
        modelBuilder.Entity<StrategyParameterVersion>(ConfigureParameterVersion);
        modelBuilder.Entity<CommandAuditLog>(ConfigureCommandAudit);

        base.OnModelCreating(modelBuilder);
    }

    private static void ConfigureDecisionLog(EntityTypeBuilder<StrategyDecisionLog> entity)
    {
        entity.ToTable("StrategyDecisionLogs");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.StrategyId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(x => x.Action)
            .HasMaxLength(64)
            .IsRequired();

        entity.Property(x => x.Reason)
            .HasMaxLength(512)
            .IsRequired();

        entity.Property(x => x.MarketId)
            .HasMaxLength(128);

        entity.Property(x => x.ContextJson)
            .HasColumnType("jsonb");

        entity.Property(x => x.ConfigVersion)
            .HasMaxLength(64)
            .IsRequired();

        entity.Property(x => x.CorrelationId)
            .HasMaxLength(256);

        entity.Property(x => x.ExecutionMode)
            .HasMaxLength(32);

        entity.Property(x => x.RunSessionId);

        entity.Property(x => x.CreatedAtUtc)
            .IsRequired();

        entity.HasIndex(x => new { x.StrategyId, x.CreatedAtUtc })
            .HasDatabaseName("IX_StrategyDecisionLogs_Strategy_Time");

        entity.HasIndex(x => x.CorrelationId)
            .HasDatabaseName("IX_StrategyDecisionLogs_CorrelationId");

        entity.HasIndex(x => x.RunSessionId)
            .HasDatabaseName("IX_StrategyDecisionLogs_RunSessionId");
    }

    private static void ConfigureObservationLog(EntityTypeBuilder<StrategyObservationLog> entity)
    {
        entity.ToTable("StrategyObservationLogs");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.StrategyId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(x => x.MarketId)
            .HasMaxLength(128);

        entity.Property(x => x.Phase)
            .HasMaxLength(64)
            .IsRequired();

        entity.Property(x => x.Outcome)
            .HasMaxLength(64)
            .IsRequired();

        entity.Property(x => x.ReasonCode)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(x => x.FeaturesJson)
            .HasColumnType("jsonb");

        entity.Property(x => x.StateJson)
            .HasColumnType("jsonb");

        entity.Property(x => x.CorrelationId)
            .HasMaxLength(256);

        entity.Property(x => x.ConfigVersion)
            .HasMaxLength(64)
            .IsRequired();

        entity.Property(x => x.ExecutionMode)
            .HasMaxLength(32);

        entity.Property(x => x.CreatedAtUtc)
            .IsRequired();

        entity.HasIndex(x => new { x.StrategyId, x.CreatedAtUtc })
            .HasDatabaseName("IX_StrategyObservationLogs_Strategy_Time");

        entity.HasIndex(x => new { x.StrategyId, x.ConfigVersion, x.CreatedAtUtc })
            .HasDatabaseName("IX_StrategyObservationLogs_Strategy_Config_Time");

        entity.HasIndex(x => new { x.StrategyId, x.ReasonCode, x.CreatedAtUtc })
            .HasDatabaseName("IX_StrategyObservationLogs_Strategy_Reason_Time");

        entity.HasIndex(x => x.CorrelationId)
            .HasDatabaseName("IX_StrategyObservationLogs_CorrelationId");
    }

    private static void ConfigurePaperRunSession(EntityTypeBuilder<PaperRunSession> entity)
    {
        entity.ToTable("PaperRunSessions");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.ExecutionMode)
            .HasMaxLength(32)
            .IsRequired();

        entity.Property(x => x.ConfigVersion)
            .HasMaxLength(64)
            .IsRequired();

        entity.Property(x => x.StrategiesJson)
            .HasColumnType("jsonb")
            .IsRequired();

        entity.Property(x => x.RiskProfileJson)
            .HasColumnType("jsonb")
            .IsRequired();

        entity.Property(x => x.OperatorSource)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(x => x.StartedAtUtc)
            .IsRequired();

        entity.Property(x => x.StoppedAtUtc);

        entity.Property(x => x.StopReason)
            .HasMaxLength(512);

        entity.HasIndex(x => new { x.ExecutionMode, x.StoppedAtUtc })
            .HasDatabaseName("IX_PaperRunSessions_Mode_Stop");

        entity.HasIndex(x => x.ExecutionMode)
            .IsUnique()
            .HasFilter("\"StoppedAtUtc\" IS NULL")
            .HasDatabaseName("IX_PaperRunSessions_ActiveMode");

        entity.HasIndex(x => x.StartedAtUtc)
            .HasDatabaseName("IX_PaperRunSessions_StartedAt");
    }

    private static void ConfigureRunState(EntityTypeBuilder<StrategyRunState> entity)
    {
        entity.ToTable("StrategyRunStates");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.StrategyId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(x => x.Name)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(x => x.State)
            .HasMaxLength(32)
            .IsRequired();

        entity.Property(x => x.DesiredState)
            .HasMaxLength(32)
            .IsRequired();

        entity.Property(x => x.ConfigVersion)
            .HasMaxLength(64)
            .IsRequired();

        entity.Property(x => x.LastError)
            .HasMaxLength(512);

        entity.Property(x => x.ActiveMarketsJson)
            .HasColumnType("jsonb");

        entity.Property(x => x.CycleCount)
            .IsRequired();

        entity.Property(x => x.SnapshotsProcessed)
            .IsRequired();

        entity.Property(x => x.ChannelBacklog)
            .IsRequired();

        entity.Property(x => x.BlockedReasonKind)
            .HasMaxLength(32);

        entity.Property(x => x.BlockedReasonCode)
            .HasMaxLength(64);

        entity.Property(x => x.BlockedReasonMessage)
            .HasMaxLength(512);

        entity.Property(x => x.UpdatedAtUtc)
            .IsRequired();

        entity.HasIndex(x => x.StrategyId)
            .IsUnique()
            .HasDatabaseName("IX_StrategyRunStates_StrategyId");
    }

    private static void ConfigureParameterVersion(EntityTypeBuilder<StrategyParameterVersion> entity)
    {
        entity.ToTable("StrategyParameterVersions");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.StrategyId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(x => x.ConfigVersion)
            .HasMaxLength(64)
            .IsRequired();

        entity.Property(x => x.PreviousConfigVersion)
            .HasMaxLength(64);

        entity.Property(x => x.SnapshotJson)
            .HasColumnType("jsonb")
            .IsRequired();

        entity.Property(x => x.DiffJson)
            .HasColumnType("jsonb")
            .IsRequired();

        entity.Property(x => x.ChangeType)
            .HasMaxLength(32)
            .IsRequired();

        entity.Property(x => x.Source)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(x => x.Actor)
            .HasMaxLength(128);

        entity.Property(x => x.Reason)
            .HasMaxLength(512);

        entity.Property(x => x.CreatedAtUtc)
            .IsRequired();

        entity.HasIndex(x => new { x.StrategyId, x.CreatedAtUtc })
            .HasDatabaseName("IX_StrategyParameterVersions_Strategy_Time");

        entity.HasIndex(x => new { x.StrategyId, x.ConfigVersion })
            .IsUnique()
            .HasDatabaseName("IX_StrategyParameterVersions_Strategy_ConfigVersion");
    }

    private static void ConfigureCommandAudit(EntityTypeBuilder<CommandAuditLog> entity)
    {
        entity.ToTable("CommandAuditLogs");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.CommandName)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(x => x.ArgumentsJson)
            .HasColumnType("jsonb")
            .IsRequired();

        entity.Property(x => x.Actor)
            .HasMaxLength(128);

        entity.Property(x => x.CreatedAtUtc)
            .IsRequired();

        entity.HasIndex(x => x.CreatedAtUtc)
            .HasDatabaseName("IX_CommandAuditLogs_CreatedAt");
    }
}
