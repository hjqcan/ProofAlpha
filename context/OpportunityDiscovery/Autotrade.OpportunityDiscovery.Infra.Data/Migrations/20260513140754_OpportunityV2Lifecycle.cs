using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autotrade.OpportunityDiscovery.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class OpportunityV2Lifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExecutableOpportunityPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HypothesisId = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MarketId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FairProbability = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    Edge = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    EntryMaxPrice = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    TakeProfitPrice = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    StopLossPrice = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    MaxSpread = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    MaxNotional = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    ValidFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ValidUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EvidenceIdsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutableOpportunityPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpportunityEvaluationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HypothesisId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RunVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MarketTapeSliceId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ReplaySeed = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpportunityEvaluationRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpportunityFeatureSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HypothesisId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketTapeSliceId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FeatureVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FeaturesJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpportunityFeatureSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpportunityHypotheses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResearchRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketTapeSliceId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PromptVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ModelVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScoreVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReplaySeed = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Thesis = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    ActivePolicyId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActiveLiveAllocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpportunityHypotheses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpportunityLifecycleTransitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HypothesisId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ToStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Actor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    EvidenceIdsJson = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpportunityLifecycleTransitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpportunityLiveAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HypothesisId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutablePolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaxNotional = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    MaxContracts = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    ValidUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpportunityLiveAllocations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpportunityPromotionGates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HypothesisId = table.Column<Guid>(type: "uuid", nullable: false),
                    GateKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Evaluator = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    MetricsJson = table.Column<string>(type: "jsonb", nullable: false),
                    EvidenceIdsJson = table.Column<string>(type: "jsonb", nullable: false),
                    EvaluatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpportunityPromotionGates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpportunityScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HypothesisId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeatureSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScoreVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FairProbability = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    Edge = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    CalibrationBucket = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ComponentsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpportunityScores", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutableOpportunityPolicies_HypothesisId",
                table: "ExecutableOpportunityPolicies",
                column: "HypothesisId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutableOpportunityPolicies_Market_Status",
                table: "ExecutableOpportunityPolicies",
                columns: new[] { "MarketId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutableOpportunityPolicies_Status_ValidUntil",
                table: "ExecutableOpportunityPolicies",
                columns: new[] { "Status", "ValidUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityEvaluationRuns_Hypothesis_Kind_Time",
                table: "OpportunityEvaluationRuns",
                columns: new[] { "HypothesisId", "EvaluationKind", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityFeatureSnapshots_Hypothesis_Time",
                table: "OpportunityFeatureSnapshots",
                columns: new[] { "HypothesisId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityHypotheses_Market_Status",
                table: "OpportunityHypotheses",
                columns: new[] { "MarketId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityHypotheses_RunId",
                table: "OpportunityHypotheses",
                column: "ResearchRunId");

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityHypotheses_Status_Time",
                table: "OpportunityHypotheses",
                columns: new[] { "Status", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityLifecycleTransitions_Hypothesis_Time",
                table: "OpportunityLifecycleTransitions",
                columns: new[] { "HypothesisId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityLiveAllocations_HypothesisId",
                table: "OpportunityLiveAllocations",
                column: "HypothesisId");

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityLiveAllocations_PolicyId",
                table: "OpportunityLiveAllocations",
                column: "ExecutablePolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityLiveAllocations_Status_ValidUntil",
                table: "OpportunityLiveAllocations",
                columns: new[] { "Status", "ValidUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityPromotionGates_Hypothesis_Kind_Time",
                table: "OpportunityPromotionGates",
                columns: new[] { "HypothesisId", "GateKind", "EvaluatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityScores_Hypothesis_Time",
                table: "OpportunityScores",
                columns: new[] { "HypothesisId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExecutableOpportunityPolicies");

            migrationBuilder.DropTable(
                name: "OpportunityEvaluationRuns");

            migrationBuilder.DropTable(
                name: "OpportunityFeatureSnapshots");

            migrationBuilder.DropTable(
                name: "OpportunityHypotheses");

            migrationBuilder.DropTable(
                name: "OpportunityLifecycleTransitions");

            migrationBuilder.DropTable(
                name: "OpportunityLiveAllocations");

            migrationBuilder.DropTable(
                name: "OpportunityPromotionGates");

            migrationBuilder.DropTable(
                name: "OpportunityScores");
        }
    }
}
