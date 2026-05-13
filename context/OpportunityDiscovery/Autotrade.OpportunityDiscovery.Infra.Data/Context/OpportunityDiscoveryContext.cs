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

    public DbSet<SourceProfile> SourceProfiles => Set<SourceProfile>();

    public DbSet<SourceObservation> SourceObservations => Set<SourceObservation>();

    public DbSet<EvidenceSnapshot> EvidenceSnapshots => Set<EvidenceSnapshot>();

    public DbSet<EvidenceCitation> EvidenceCitations => Set<EvidenceCitation>();

    public DbSet<EvidenceConflict> EvidenceConflicts => Set<EvidenceConflict>();

    public DbSet<OfficialConfirmation> OfficialConfirmations => Set<OfficialConfirmation>();

    public DbSet<OpportunityHypothesis> OpportunityHypotheses => Set<OpportunityHypothesis>();

    public DbSet<OpportunityLifecycleTransition> OpportunityLifecycleTransitions => Set<OpportunityLifecycleTransition>();

    public DbSet<OpportunityFeatureSnapshot> OpportunityFeatureSnapshots => Set<OpportunityFeatureSnapshot>();

    public DbSet<OpportunityScore> OpportunityScores => Set<OpportunityScore>();

    public DbSet<OpportunityEvaluationRun> OpportunityEvaluationRuns => Set<OpportunityEvaluationRun>();

    public DbSet<OpportunityPromotionGate> OpportunityPromotionGates => Set<OpportunityPromotionGate>();

    public DbSet<ExecutableOpportunityPolicy> ExecutableOpportunityPolicies => Set<ExecutableOpportunityPolicy>();

    public DbSet<OpportunityLiveAllocation> OpportunityLiveAllocations => Set<OpportunityLiveAllocation>();

    public DbSet<MarketOpportunity> MarketOpportunities => Set<MarketOpportunity>();

    public DbSet<OpportunityReview> OpportunityReviews => Set<OpportunityReview>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Ignore<Event>();
        modelBuilder.Entity<ResearchRun>(ConfigureResearchRun);
        modelBuilder.Entity<EvidenceItem>(ConfigureEvidenceItem);
        modelBuilder.Entity<SourceProfile>(ConfigureSourceProfile);
        modelBuilder.Entity<SourceObservation>(ConfigureSourceObservation);
        modelBuilder.Entity<EvidenceSnapshot>(ConfigureEvidenceSnapshot);
        modelBuilder.Entity<EvidenceCitation>(ConfigureEvidenceCitation);
        modelBuilder.Entity<EvidenceConflict>(ConfigureEvidenceConflict);
        modelBuilder.Entity<OfficialConfirmation>(ConfigureOfficialConfirmation);
        modelBuilder.Entity<OpportunityHypothesis>(ConfigureOpportunityHypothesis);
        modelBuilder.Entity<OpportunityLifecycleTransition>(ConfigureOpportunityLifecycleTransition);
        modelBuilder.Entity<OpportunityFeatureSnapshot>(ConfigureOpportunityFeatureSnapshot);
        modelBuilder.Entity<OpportunityScore>(ConfigureOpportunityScore);
        modelBuilder.Entity<OpportunityEvaluationRun>(ConfigureOpportunityEvaluationRun);
        modelBuilder.Entity<OpportunityPromotionGate>(ConfigureOpportunityPromotionGate);
        modelBuilder.Entity<ExecutableOpportunityPolicy>(ConfigureExecutableOpportunityPolicy);
        modelBuilder.Entity<OpportunityLiveAllocation>(ConfigureOpportunityLiveAllocation);
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

    private static void ConfigureSourceProfile(EntityTypeBuilder<SourceProfile> entity)
    {
        entity.ToTable("OpportunitySourceProfiles");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.SourceKey).HasMaxLength(128).IsRequired();
        entity.Property(x => x.SourceKind).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.SourceName).HasMaxLength(128).IsRequired();
        entity.Property(x => x.AuthorityKind).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.IsOfficial).IsRequired();
        entity.Property(x => x.ExpectedLatencySeconds).IsRequired();
        entity.Property(x => x.CoveredCategoriesJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.HistoricalConflictRate).HasPrecision(8, 4).IsRequired();
        entity.Property(x => x.HistoricalPassedGateContribution).HasPrecision(8, 4).IsRequired();
        entity.Property(x => x.ReliabilityScore).HasPrecision(8, 4).IsRequired();
        entity.Property(x => x.Version).IsRequired();
        entity.Property(x => x.ChangeReason).HasMaxLength(1024).IsRequired();
        entity.Property(x => x.CreatedAtUtc).IsRequired();
        entity.HasIndex(x => x.SourceKey).HasDatabaseName("IX_OpportunitySourceProfiles_SourceKey");
        entity.HasIndex(x => new { x.SourceKey, x.Version })
            .IsUnique()
            .HasDatabaseName("IX_OpportunitySourceProfiles_SourceKey_Version");
    }

    private static void ConfigureSourceObservation(EntityTypeBuilder<SourceObservation> entity)
    {
        entity.ToTable("OpportunitySourceObservations");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.SourceKey).HasMaxLength(128).IsRequired();
        entity.Property(x => x.ObservationKind).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.ObservedAtUtc).IsRequired();
        entity.Property(x => x.Confidence).HasPrecision(8, 4).IsRequired();
        entity.Property(x => x.ObservationJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.CreatedAtUtc).IsRequired();
        entity.HasIndex(x => new { x.SourceKey, x.ObservedAtUtc })
            .HasDatabaseName("IX_OpportunitySourceObservations_Source_Time");
        entity.HasIndex(x => x.OpportunityId)
            .HasDatabaseName("IX_OpportunitySourceObservations_OpportunityId");
    }

    private static void ConfigureEvidenceSnapshot(EntityTypeBuilder<EvidenceSnapshot> entity)
    {
        entity.ToTable("OpportunityEvidenceSnapshots");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.MarketId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.SnapshotAsOfUtc).IsRequired();
        entity.Property(x => x.LiveGateStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.LiveGateReasonsJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.SummaryJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.CreatedAtUtc).IsRequired();
        entity.HasIndex(x => new { x.OpportunityId, x.SnapshotAsOfUtc })
            .HasDatabaseName("IX_OpportunityEvidenceSnapshots_Opportunity_AsOf");
        entity.HasIndex(x => new { x.MarketId, x.SnapshotAsOfUtc })
            .HasDatabaseName("IX_OpportunityEvidenceSnapshots_Market_AsOf");
    }

    private static void ConfigureEvidenceCitation(EntityTypeBuilder<EvidenceCitation> entity)
    {
        entity.ToTable("OpportunityEvidenceCitations");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.SourceKey).HasMaxLength(128).IsRequired();
        entity.Property(x => x.SourceKind).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.SourceName).HasMaxLength(128).IsRequired();
        entity.Property(x => x.IsOfficial).IsRequired();
        entity.Property(x => x.AuthorityKind).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.Url).HasMaxLength(2048).IsRequired();
        entity.Property(x => x.Title).HasMaxLength(512).IsRequired();
        entity.Property(x => x.ObservedAtUtc).IsRequired();
        entity.Property(x => x.ContentHash).HasMaxLength(128).IsRequired();
        entity.Property(x => x.RelevanceScore).HasPrecision(8, 4).IsRequired();
        entity.Property(x => x.ClaimJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.CreatedAtUtc).IsRequired();
        entity.HasIndex(x => x.EvidenceSnapshotId)
            .HasDatabaseName("IX_OpportunityEvidenceCitations_SnapshotId");
        entity.HasIndex(x => new { x.SourceKey, x.ObservedAtUtc })
            .HasDatabaseName("IX_OpportunityEvidenceCitations_Source_Time");
    }

    private static void ConfigureEvidenceConflict(EntityTypeBuilder<EvidenceConflict> entity)
    {
        entity.ToTable("OpportunityEvidenceConflicts");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.ConflictKey).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Severity).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.Description).HasMaxLength(2048).IsRequired();
        entity.Property(x => x.SourceKeysJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.BlocksLivePromotion).IsRequired();
        entity.Property(x => x.DetectedAtUtc).IsRequired();
        entity.HasIndex(x => x.EvidenceSnapshotId)
            .HasDatabaseName("IX_OpportunityEvidenceConflicts_SnapshotId");
        entity.HasIndex(x => new { x.EvidenceSnapshotId, x.Severity })
            .HasDatabaseName("IX_OpportunityEvidenceConflicts_Snapshot_Severity");
    }

    private static void ConfigureOfficialConfirmation(EntityTypeBuilder<OfficialConfirmation> entity)
    {
        entity.ToTable("OpportunityOfficialConfirmations");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.SourceKey).HasMaxLength(128).IsRequired();
        entity.Property(x => x.ConfirmationKind).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.Claim).HasMaxLength(2048).IsRequired();
        entity.Property(x => x.Url).HasMaxLength(2048).IsRequired();
        entity.Property(x => x.Confidence).HasPrecision(8, 4).IsRequired();
        entity.Property(x => x.ConfirmedAtUtc).IsRequired();
        entity.Property(x => x.RawJson).HasColumnType("jsonb").IsRequired();
        entity.HasIndex(x => x.EvidenceSnapshotId)
            .HasDatabaseName("IX_OpportunityOfficialConfirmations_SnapshotId");
        entity.HasIndex(x => new { x.SourceKey, x.ConfirmedAtUtc })
            .HasDatabaseName("IX_OpportunityOfficialConfirmations_Source_Time");
    }

    private static void ConfigureOpportunityHypothesis(EntityTypeBuilder<OpportunityHypothesis> entity)
    {
        entity.ToTable("OpportunityHypotheses");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.MarketId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(16).IsRequired();
        entity.Property(x => x.MarketTapeSliceId).HasMaxLength(256).IsRequired();
        entity.Property(x => x.PromptVersion).HasMaxLength(64).IsRequired();
        entity.Property(x => x.ModelVersion).HasMaxLength(64).IsRequired();
        entity.Property(x => x.ScoreVersion).HasMaxLength(64);
        entity.Property(x => x.ReplaySeed).HasMaxLength(128);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.Thesis).HasMaxLength(4096).IsRequired();
        entity.Property(x => x.CreatedAtUtc).IsRequired();
        entity.Property(x => x.UpdatedAtUtc).IsRequired();
        entity.HasIndex(x => x.ResearchRunId).HasDatabaseName("IX_OpportunityHypotheses_RunId");
        entity.HasIndex(x => new { x.Status, x.UpdatedAtUtc }).HasDatabaseName("IX_OpportunityHypotheses_Status_Time");
        entity.HasIndex(x => new { x.MarketId, x.Status }).HasDatabaseName("IX_OpportunityHypotheses_Market_Status");
    }

    private static void ConfigureOpportunityLifecycleTransition(EntityTypeBuilder<OpportunityLifecycleTransition> entity)
    {
        entity.ToTable("OpportunityLifecycleTransitions");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.Actor).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Reason).HasMaxLength(2048).IsRequired();
        entity.Property(x => x.EvidenceIdsJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.OccurredAtUtc).IsRequired();
        entity.HasIndex(x => new { x.HypothesisId, x.OccurredAtUtc })
            .HasDatabaseName("IX_OpportunityLifecycleTransitions_Hypothesis_Time");
    }

    private static void ConfigureOpportunityFeatureSnapshot(EntityTypeBuilder<OpportunityFeatureSnapshot> entity)
    {
        entity.ToTable("OpportunityFeatureSnapshots");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.MarketTapeSliceId).HasMaxLength(256).IsRequired();
        entity.Property(x => x.FeatureVersion).HasMaxLength(64).IsRequired();
        entity.Property(x => x.FeaturesJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.CreatedAtUtc).IsRequired();
        entity.HasIndex(x => new { x.HypothesisId, x.CreatedAtUtc })
            .HasDatabaseName("IX_OpportunityFeatureSnapshots_Hypothesis_Time");
    }

    private static void ConfigureOpportunityScore(EntityTypeBuilder<OpportunityScore> entity)
    {
        entity.ToTable("OpportunityScores");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.ScoreVersion).HasMaxLength(64).IsRequired();
        entity.Property(x => x.LlmFairProbability).HasPrecision(8, 4).IsRequired();
        entity.Property(x => x.FairProbability).HasPrecision(8, 4).IsRequired();
        entity.Property(x => x.Confidence).HasPrecision(8, 4).IsRequired();
        entity.Property(x => x.Edge).HasPrecision(8, 4).IsRequired();
        entity.Property(x => x.MarketImpliedProbability).HasPrecision(8, 4).IsRequired();
        entity.Property(x => x.ExecutableEntryPrice).HasPrecision(8, 4).IsRequired();
        entity.Property(x => x.FeeEstimate).HasPrecision(8, 4).IsRequired();
        entity.Property(x => x.SlippageBuffer).HasPrecision(8, 4).IsRequired();
        entity.Property(x => x.NetEdge).HasPrecision(8, 4).IsRequired();
        entity.Property(x => x.ExecutableCapacity).HasPrecision(18, 8).IsRequired();
        entity.Property(x => x.CanPromote).IsRequired();
        entity.Property(x => x.CalibrationBucket).HasMaxLength(128).IsRequired();
        entity.Property(x => x.ComponentsJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.CreatedAtUtc).IsRequired();
        entity.HasIndex(x => new { x.HypothesisId, x.CreatedAtUtc })
            .HasDatabaseName("IX_OpportunityScores_Hypothesis_Time");
        entity.HasIndex(x => new { x.CanPromote, x.NetEdge })
            .HasDatabaseName("IX_OpportunityScores_CanPromote_NetEdge");
    }

    private static void ConfigureOpportunityEvaluationRun(EntityTypeBuilder<OpportunityEvaluationRun> entity)
    {
        entity.ToTable("OpportunityEvaluationRuns");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.EvaluationKind).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.RunVersion).HasMaxLength(64).IsRequired();
        entity.Property(x => x.MarketTapeSliceId).HasMaxLength(256).IsRequired();
        entity.Property(x => x.ReplaySeed).HasMaxLength(128).IsRequired();
        entity.Property(x => x.ResultJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.ErrorMessage).HasMaxLength(2048);
        entity.Property(x => x.StartedAtUtc).IsRequired();
        entity.Property(x => x.UpdatedAtUtc).IsRequired();
        entity.HasIndex(x => new { x.HypothesisId, x.EvaluationKind, x.StartedAtUtc })
            .HasDatabaseName("IX_OpportunityEvaluationRuns_Hypothesis_Kind_Time");
    }

    private static void ConfigureOpportunityPromotionGate(EntityTypeBuilder<OpportunityPromotionGate> entity)
    {
        entity.ToTable("OpportunityPromotionGates");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.GateKind).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.Evaluator).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Reason).HasMaxLength(2048).IsRequired();
        entity.Property(x => x.MetricsJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.EvidenceIdsJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.EvaluatedAtUtc).IsRequired();
        entity.HasIndex(x => new { x.HypothesisId, x.GateKind, x.EvaluatedAtUtc })
            .HasDatabaseName("IX_OpportunityPromotionGates_Hypothesis_Kind_Time");
    }

    private static void ConfigureExecutableOpportunityPolicy(EntityTypeBuilder<ExecutableOpportunityPolicy> entity)
    {
        entity.ToTable("ExecutableOpportunityPolicies");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.PolicyVersion).HasMaxLength(64).IsRequired();
        entity.Property(x => x.MarketId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(16).IsRequired();
        entity.Property(x => x.FairProbability).HasPrecision(8, 4).IsRequired();
        entity.Property(x => x.Confidence).HasPrecision(8, 4).IsRequired();
        entity.Property(x => x.Edge).HasPrecision(8, 4).IsRequired();
        entity.Property(x => x.EntryMaxPrice).HasPrecision(8, 4).IsRequired();
        entity.Property(x => x.TakeProfitPrice).HasPrecision(8, 4).IsRequired();
        entity.Property(x => x.StopLossPrice).HasPrecision(8, 4).IsRequired();
        entity.Property(x => x.MaxSpread).HasPrecision(8, 4).IsRequired();
        entity.Property(x => x.Quantity).HasPrecision(18, 8).IsRequired();
        entity.Property(x => x.MaxNotional).HasPrecision(18, 8).IsRequired();
        entity.Property(x => x.EvidenceIdsJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.CreatedAtUtc).IsRequired();
        entity.Property(x => x.UpdatedAtUtc).IsRequired();
        entity.HasIndex(x => new { x.Status, x.ValidUntilUtc })
            .HasDatabaseName("IX_ExecutableOpportunityPolicies_Status_ValidUntil");
        entity.HasIndex(x => new { x.MarketId, x.Status })
            .HasDatabaseName("IX_ExecutableOpportunityPolicies_Market_Status");
        entity.HasIndex(x => x.HypothesisId)
            .HasDatabaseName("IX_ExecutableOpportunityPolicies_HypothesisId");
    }

    private static void ConfigureOpportunityLiveAllocation(EntityTypeBuilder<OpportunityLiveAllocation> entity)
    {
        entity.ToTable("OpportunityLiveAllocations");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.MaxNotional).HasPrecision(18, 8).IsRequired();
        entity.Property(x => x.MaxContracts).HasPrecision(18, 8).IsRequired();
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.Reason).HasMaxLength(2048).IsRequired();
        entity.Property(x => x.CreatedAtUtc).IsRequired();
        entity.Property(x => x.UpdatedAtUtc).IsRequired();
        entity.HasIndex(x => new { x.Status, x.ValidUntilUtc })
            .HasDatabaseName("IX_OpportunityLiveAllocations_Status_ValidUntil");
        entity.HasIndex(x => x.HypothesisId)
            .HasDatabaseName("IX_OpportunityLiveAllocations_HypothesisId");
        entity.HasIndex(x => x.ExecutablePolicyId)
            .HasDatabaseName("IX_OpportunityLiveAllocations_PolicyId");
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
