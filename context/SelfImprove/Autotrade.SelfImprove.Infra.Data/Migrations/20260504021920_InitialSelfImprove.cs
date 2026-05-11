using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autotrade.SelfImprove.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSelfImprove : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GeneratedStrategyVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    StrategyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ArtifactRoot = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    PackageHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ManifestJson = table.Column<string>(type: "jsonb", nullable: false),
                    RiskEnvelopeJson = table.Column<string>(type: "jsonb", nullable: false),
                    ValidationSummaryJson = table.Column<string>(type: "jsonb", nullable: true),
                    IsActiveCanary = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    QuarantineReason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneratedStrategyVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImprovementProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    StrategyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RiskLevel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Rationale = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    ExpectedImpactJson = table.Column<string>(type: "jsonb", nullable: false),
                    RollbackConditionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ParameterPatchJson = table.Column<string>(type: "jsonb", nullable: true),
                    CodeGenerationSpecJson = table.Column<string>(type: "jsonb", nullable: true),
                    RequiresManualReview = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AppliedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImprovementProposals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImprovementRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StrategyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MarketId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    WindowStartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    WindowEndUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Trigger = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProposalCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImprovementRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ParameterPatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    StrategyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Path = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OldValueJson = table.Column<string>(type: "jsonb", nullable: true),
                    NewValueJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParameterPatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PatchOutcomes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    StrategyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DiffJson = table.Column<string>(type: "jsonb", nullable: false),
                    RollbackJson = table.Column<string>(type: "jsonb", nullable: true),
                    Message = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatchOutcomes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PromotionGateResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GeneratedStrategyVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Passed = table.Column<bool>(type: "boolean", nullable: false),
                    Message = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionGateResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StrategyEpisodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StrategyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MarketId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ConfigVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WindowStartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    WindowEndUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecisionCount = table.Column<int>(type: "integer", nullable: false),
                    ObservationCount = table.Column<int>(type: "integer", nullable: false),
                    OrderCount = table.Column<int>(type: "integer", nullable: false),
                    TradeCount = table.Column<int>(type: "integer", nullable: false),
                    RiskEventCount = table.Column<int>(type: "integer", nullable: false),
                    NetPnl = table.Column<decimal>(type: "numeric", nullable: false),
                    FillRate = table.Column<decimal>(type: "numeric", nullable: false),
                    RejectRate = table.Column<decimal>(type: "numeric", nullable: false),
                    TimeoutRate = table.Column<decimal>(type: "numeric", nullable: false),
                    MaxOpenExposure = table.Column<decimal>(type: "numeric", nullable: false),
                    DrawdownLike = table.Column<decimal>(type: "numeric", nullable: false),
                    SourceIdsJson = table.Column<string>(type: "jsonb", nullable: false),
                    MetricsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyEpisodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StrategyMemories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StrategyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MemoryJson = table.Column<string>(type: "jsonb", nullable: false),
                    PlaybookJson = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyMemories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedStrategyVersions_Stage_Canary",
                table: "GeneratedStrategyVersions",
                columns: new[] { "Stage", "IsActiveCanary" });

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedStrategyVersions_Strategy_Version",
                table: "GeneratedStrategyVersions",
                columns: new[] { "StrategyId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImprovementProposals_RunId",
                table: "ImprovementProposals",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_ImprovementProposals_Strategy_Status",
                table: "ImprovementProposals",
                columns: new[] { "StrategyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ImprovementRuns_Status",
                table: "ImprovementRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ImprovementRuns_Strategy_Time",
                table: "ImprovementRuns",
                columns: new[] { "StrategyId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ParameterPatches_ProposalId",
                table: "ParameterPatches",
                column: "ProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_PatchOutcomes_ProposalId",
                table: "PatchOutcomes",
                column: "ProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionGateResults_GeneratedVersionId",
                table: "PromotionGateResults",
                column: "GeneratedStrategyVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_StrategyEpisodes_Strategy_Window",
                table: "StrategyEpisodes",
                columns: new[] { "StrategyId", "WindowStartUtc", "WindowEndUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StrategyMemories_StrategyId",
                table: "StrategyMemories",
                column: "StrategyId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GeneratedStrategyVersions");

            migrationBuilder.DropTable(
                name: "ImprovementProposals");

            migrationBuilder.DropTable(
                name: "ImprovementRuns");

            migrationBuilder.DropTable(
                name: "ParameterPatches");

            migrationBuilder.DropTable(
                name: "PatchOutcomes");

            migrationBuilder.DropTable(
                name: "PromotionGateResults");

            migrationBuilder.DropTable(
                name: "StrategyEpisodes");

            migrationBuilder.DropTable(
                name: "StrategyMemories");
        }
    }
}
