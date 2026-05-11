using Autotrade.Domain.Abstractions.EventBus;
using Autotrade.Infra.Data.Core.Context;
using Autotrade.SelfImprove.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Logging;
using NetDevPack.Messaging;

namespace Autotrade.SelfImprove.Infra.Data.Context;

public sealed class SelfImproveContext : BaseDbContext
{
    public const string MigrationsHistoryTable = "__EFMigrationsHistory_SelfImprove";

    public SelfImproveContext(
        DbContextOptions<SelfImproveContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        ILogger<SelfImproveContext> logger,
        IIntegrationEventPublisher? integrationEventPublisher = null)
        : base(options, domainEventDispatcher, integrationEventPublisher, logger)
    {
        ChangeTracker.AutoDetectChangesEnabled = false;
    }

    public DbSet<ImprovementRun> ImprovementRuns => Set<ImprovementRun>();

    public DbSet<StrategyEpisode> StrategyEpisodes => Set<StrategyEpisode>();

    public DbSet<StrategyMemory> StrategyMemories => Set<StrategyMemory>();

    public DbSet<ImprovementProposal> ImprovementProposals => Set<ImprovementProposal>();

    public DbSet<ParameterPatch> ParameterPatches => Set<ParameterPatch>();

    public DbSet<GeneratedStrategyVersion> GeneratedStrategyVersions => Set<GeneratedStrategyVersion>();

    public DbSet<PromotionGateResult> PromotionGateResults => Set<PromotionGateResult>();

    public DbSet<PatchOutcome> PatchOutcomes => Set<PatchOutcome>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Ignore<Event>();

        modelBuilder.Entity<ImprovementRun>(ConfigureImprovementRun);
        modelBuilder.Entity<StrategyEpisode>(ConfigureStrategyEpisode);
        modelBuilder.Entity<StrategyMemory>(ConfigureStrategyMemory);
        modelBuilder.Entity<ImprovementProposal>(ConfigureImprovementProposal);
        modelBuilder.Entity<ParameterPatch>(ConfigureParameterPatch);
        modelBuilder.Entity<GeneratedStrategyVersion>(ConfigureGeneratedStrategyVersion);
        modelBuilder.Entity<PromotionGateResult>(ConfigurePromotionGateResult);
        modelBuilder.Entity<PatchOutcome>(ConfigurePatchOutcome);

        base.OnModelCreating(modelBuilder);
    }

    private static void ConfigureImprovementRun(EntityTypeBuilder<ImprovementRun> entity)
    {
        entity.ToTable("ImprovementRuns");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.StrategyId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.MarketId).HasMaxLength(128);
        entity.Property(x => x.Trigger).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.ErrorMessage).HasMaxLength(2048);
        entity.HasIndex(x => new { x.StrategyId, x.CreatedAtUtc }).HasDatabaseName("IX_ImprovementRuns_Strategy_Time");
        entity.HasIndex(x => x.Status).HasDatabaseName("IX_ImprovementRuns_Status");
    }

    private static void ConfigureStrategyEpisode(EntityTypeBuilder<StrategyEpisode> entity)
    {
        entity.ToTable("StrategyEpisodes");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.StrategyId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.MarketId).HasMaxLength(128);
        entity.Property(x => x.ConfigVersion).HasMaxLength(64).IsRequired();
        entity.Property(x => x.SourceIdsJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.MetricsJson).HasColumnType("jsonb").IsRequired();
        entity.HasIndex(x => new { x.StrategyId, x.WindowStartUtc, x.WindowEndUtc })
            .HasDatabaseName("IX_StrategyEpisodes_Strategy_Window");
    }

    private static void ConfigureStrategyMemory(EntityTypeBuilder<StrategyMemory> entity)
    {
        entity.ToTable("StrategyMemories");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.StrategyId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.MemoryJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.PlaybookJson).HasColumnType("jsonb").IsRequired();
        entity.HasIndex(x => x.StrategyId).IsUnique().HasDatabaseName("IX_StrategyMemories_StrategyId");
    }

    private static void ConfigureImprovementProposal(EntityTypeBuilder<ImprovementProposal> entity)
    {
        entity.ToTable("ImprovementProposals");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.StrategyId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.RiskLevel).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.Title).HasMaxLength(256).IsRequired();
        entity.Property(x => x.Rationale).HasMaxLength(4096).IsRequired();
        entity.Property(x => x.EvidenceJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.ExpectedImpactJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.RollbackConditionsJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.ParameterPatchJson).HasColumnType("jsonb");
        entity.Property(x => x.CodeGenerationSpecJson).HasColumnType("jsonb");
        entity.HasIndex(x => x.RunId).HasDatabaseName("IX_ImprovementProposals_RunId");
        entity.HasIndex(x => new { x.StrategyId, x.Status }).HasDatabaseName("IX_ImprovementProposals_Strategy_Status");
    }

    private static void ConfigureParameterPatch(EntityTypeBuilder<ParameterPatch> entity)
    {
        entity.ToTable("ParameterPatches");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.StrategyId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Path).HasMaxLength(256).IsRequired();
        entity.Property(x => x.OldValueJson).HasColumnType("jsonb");
        entity.Property(x => x.NewValueJson).HasColumnType("jsonb").IsRequired();
        entity.HasIndex(x => x.ProposalId).HasDatabaseName("IX_ParameterPatches_ProposalId");
    }

    private static void ConfigureGeneratedStrategyVersion(EntityTypeBuilder<GeneratedStrategyVersion> entity)
    {
        entity.ToTable("GeneratedStrategyVersions");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.StrategyId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Version).HasMaxLength(64).IsRequired();
        entity.Property(x => x.Stage).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.ArtifactRoot).HasMaxLength(1024).IsRequired();
        entity.Property(x => x.PackageHash).HasMaxLength(128).IsRequired();
        entity.Property(x => x.ManifestJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.RiskEnvelopeJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.ValidationSummaryJson).HasColumnType("jsonb");
        entity.Property(x => x.QuarantineReason).HasMaxLength(2048);
        entity.HasIndex(x => new { x.StrategyId, x.Version }).IsUnique()
            .HasDatabaseName("IX_GeneratedStrategyVersions_Strategy_Version");
        entity.HasIndex(x => new { x.Stage, x.IsActiveCanary }).HasDatabaseName("IX_GeneratedStrategyVersions_Stage_Canary");
    }

    private static void ConfigurePromotionGateResult(EntityTypeBuilder<PromotionGateResult> entity)
    {
        entity.ToTable("PromotionGateResults");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Stage).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.Message).HasMaxLength(2048).IsRequired();
        entity.Property(x => x.EvidenceJson).HasColumnType("jsonb").IsRequired();
        entity.HasIndex(x => x.GeneratedStrategyVersionId).HasDatabaseName("IX_PromotionGateResults_GeneratedVersionId");
    }

    private static void ConfigurePatchOutcome(EntityTypeBuilder<PatchOutcome> entity)
    {
        entity.ToTable("PatchOutcomes");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.StrategyId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.DiffJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.RollbackJson).HasColumnType("jsonb");
        entity.Property(x => x.Message).HasMaxLength(2048);
        entity.HasIndex(x => x.ProposalId).HasDatabaseName("IX_PatchOutcomes_ProposalId");
    }
}
