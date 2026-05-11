using Autotrade.Domain.Abstractions.EventBus;
using Autotrade.Infra.Data.Core.Context;
using Autotrade.OpportunityDiscovery.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Logging;
using NetDevPack.Messaging;

namespace Autotrade.OpportunityDiscovery.Infra.Data.Context;

public sealed class OpportunityDiscoveryContext : BaseDbContext
{
    public const string MigrationsHistoryTable = "__EFMigrationsHistory_OpportunityDiscovery";

    public OpportunityDiscoveryContext(
        DbContextOptions<OpportunityDiscoveryContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        ILogger<OpportunityDiscoveryContext> logger,
        IIntegrationEventPublisher? integrationEventPublisher = null)
        : base(options, domainEventDispatcher, integrationEventPublisher, logger)
    {
        ChangeTracker.AutoDetectChangesEnabled = false;
    }

    public DbSet<ResearchRun> ResearchRuns => Set<ResearchRun>();

    public DbSet<EvidenceItem> EvidenceItems => Set<EvidenceItem>();

    public DbSet<MarketOpportunity> MarketOpportunities => Set<MarketOpportunity>();

    public DbSet<OpportunityReview> OpportunityReviews => Set<OpportunityReview>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Ignore<Event>();
        modelBuilder.Entity<ResearchRun>(ConfigureResearchRun);
        modelBuilder.Entity<EvidenceItem>(ConfigureEvidenceItem);
        modelBuilder.Entity<MarketOpportunity>(ConfigureMarketOpportunity);
        modelBuilder.Entity<OpportunityReview>(ConfigureOpportunityReview);
        base.OnModelCreating(modelBuilder);
    }

    private static void ConfigureResearchRun(EntityTypeBuilder<ResearchRun> entity)
    {
        entity.ToTable("OpportunityResearchRuns");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Trigger).HasMaxLength(128).IsRequired();
        entity.Property(x => x.MarketUniverseJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.ErrorMessage).HasMaxLength(2048);
        entity.Property(x => x.CreatedAtUtc).IsRequired();
        entity.Property(x => x.UpdatedAtUtc).IsRequired();
        entity.HasIndex(x => new { x.Status, x.CreatedAtUtc }).HasDatabaseName("IX_OpportunityResearchRuns_Status_Time");
    }

    private static void ConfigureEvidenceItem(EntityTypeBuilder<EvidenceItem> entity)
    {
        entity.ToTable("OpportunityEvidenceItems");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.SourceKind).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.SourceName).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Url).HasMaxLength(2048).IsRequired();
        entity.Property(x => x.Title).HasMaxLength(512).IsRequired();
        entity.Property(x => x.Summary).HasMaxLength(4096).IsRequired();
        entity.Property(x => x.ContentHash).HasMaxLength(128).IsRequired();
        entity.Property(x => x.RawJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.SourceQuality).HasPrecision(8, 4).IsRequired();
        entity.Property(x => x.CreatedAtUtc).IsRequired();
        entity.Property(x => x.ObservedAtUtc).IsRequired();
        entity.HasIndex(x => x.ResearchRunId).HasDatabaseName("IX_OpportunityEvidenceItems_RunId");
        entity.HasIndex(x => new { x.ResearchRunId, x.ContentHash })
            .IsUnique()
            .HasDatabaseName("IX_OpportunityEvidenceItems_RunId_ContentHash");
    }

    private static void ConfigureMarketOpportunity(EntityTypeBuilder<MarketOpportunity> entity)
    {
        entity.ToTable("MarketOpportunities");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.MarketId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(16).IsRequired();
        entity.Property(x => x.FairProbability).HasPrecision(8, 4).IsRequired();
        entity.Property(x => x.Confidence).HasPrecision(8, 4).IsRequired();
        entity.Property(x => x.Edge).HasPrecision(8, 4).IsRequired();
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.Reason).HasMaxLength(2048).IsRequired();
        entity.Property(x => x.EvidenceIdsJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.LlmOutputJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.ScoreJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.CompiledPolicyJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.ValidUntilUtc).IsRequired();
        entity.Property(x => x.CreatedAtUtc).IsRequired();
        entity.Property(x => x.UpdatedAtUtc).IsRequired();
        entity.HasIndex(x => x.ResearchRunId).HasDatabaseName("IX_MarketOpportunities_RunId");
        entity.HasIndex(x => new { x.Status, x.ValidUntilUtc }).HasDatabaseName("IX_MarketOpportunities_Status_ValidUntil");
        entity.HasIndex(x => new { x.MarketId, x.Status }).HasDatabaseName("IX_MarketOpportunities_Market_Status");
    }

    private static void ConfigureOpportunityReview(EntityTypeBuilder<OpportunityReview> entity)
    {
        entity.ToTable("OpportunityReviews");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Decision).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.Actor).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Notes).HasMaxLength(2048).IsRequired();
        entity.Property(x => x.CreatedAtUtc).IsRequired();
        entity.HasIndex(x => x.OpportunityId).HasDatabaseName("IX_OpportunityReviews_OpportunityId");
    }
}
