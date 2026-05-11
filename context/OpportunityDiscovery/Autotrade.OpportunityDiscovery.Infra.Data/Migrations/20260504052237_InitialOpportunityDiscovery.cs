using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autotrade.OpportunityDiscovery.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialOpportunityDiscovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MarketOpportunities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResearchRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FairProbability = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    Edge = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ValidUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    EvidenceIdsJson = table.Column<string>(type: "jsonb", nullable: false),
                    LlmOutputJson = table.Column<string>(type: "jsonb", nullable: false),
                    ScoreJson = table.Column<string>(type: "jsonb", nullable: false),
                    CompiledPolicyJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketOpportunities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpportunityEvidenceItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResearchRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Summary = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ObservedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RawJson = table.Column<string>(type: "jsonb", nullable: false),
                    SourceQuality = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpportunityEvidenceItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpportunityResearchRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Trigger = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MarketUniverseJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EvidenceCount = table.Column<int>(type: "integer", nullable: false),
                    OpportunityCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpportunityResearchRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpportunityReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OpportunityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Actor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpportunityReviews", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarketOpportunities_Market_Status",
                table: "MarketOpportunities",
                columns: new[] { "MarketId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketOpportunities_RunId",
                table: "MarketOpportunities",
                column: "ResearchRunId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketOpportunities_Status_ValidUntil",
                table: "MarketOpportunities",
                columns: new[] { "Status", "ValidUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityEvidenceItems_RunId",
                table: "OpportunityEvidenceItems",
                column: "ResearchRunId");

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityEvidenceItems_RunId_ContentHash",
                table: "OpportunityEvidenceItems",
                columns: new[] { "ResearchRunId", "ContentHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityResearchRuns_Status_Time",
                table: "OpportunityResearchRuns",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityReviews_OpportunityId",
                table: "OpportunityReviews",
                column: "OpportunityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketOpportunities");

            migrationBuilder.DropTable(
                name: "OpportunityEvidenceItems");

            migrationBuilder.DropTable(
                name: "OpportunityResearchRuns");

            migrationBuilder.DropTable(
                name: "OpportunityReviews");
        }
    }
}
