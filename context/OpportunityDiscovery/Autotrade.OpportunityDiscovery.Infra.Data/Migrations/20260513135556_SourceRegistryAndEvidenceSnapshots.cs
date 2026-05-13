using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autotrade.OpportunityDiscovery.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class SourceRegistryAndEvidenceSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OpportunityEvidenceCitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsOfficial = table.Column<bool>(type: "boolean", nullable: false),
                    AuthorityKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ObservedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RelevanceScore = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    ClaimJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpportunityEvidenceCitations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpportunityEvidenceConflicts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConflictKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    SourceKeysJson = table.Column<string>(type: "jsonb", nullable: false),
                    BlocksLivePromotion = table.Column<bool>(type: "boolean", nullable: false),
                    DetectedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpportunityEvidenceConflicts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpportunityEvidenceSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OpportunityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResearchRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SnapshotAsOfUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LiveGateStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LiveGateReasonsJson = table.Column<string>(type: "jsonb", nullable: false),
                    SummaryJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpportunityEvidenceSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpportunityOfficialConfirmations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ConfirmationKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Claim = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    ConfirmedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RawJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpportunityOfficialConfirmations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpportunitySourceObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ObservationKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    EvidenceSnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    OpportunityId = table.Column<Guid>(type: "uuid", nullable: true),
                    ObservedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    ObservationJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpportunitySourceObservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpportunitySourceProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AuthorityKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsOfficial = table.Column<bool>(type: "boolean", nullable: false),
                    ExpectedLatencySeconds = table.Column<int>(type: "integer", nullable: false),
                    CoveredCategoriesJson = table.Column<string>(type: "jsonb", nullable: false),
                    HistoricalConflictRate = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    HistoricalPassedGateContribution = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    ReliabilityScore = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    SupersedesProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangeReason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpportunitySourceProfiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityEvidenceCitations_SnapshotId",
                table: "OpportunityEvidenceCitations",
                column: "EvidenceSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityEvidenceCitations_Source_Time",
                table: "OpportunityEvidenceCitations",
                columns: new[] { "SourceKey", "ObservedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityEvidenceConflicts_Snapshot_Severity",
                table: "OpportunityEvidenceConflicts",
                columns: new[] { "EvidenceSnapshotId", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityEvidenceConflicts_SnapshotId",
                table: "OpportunityEvidenceConflicts",
                column: "EvidenceSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityEvidenceSnapshots_Market_AsOf",
                table: "OpportunityEvidenceSnapshots",
                columns: new[] { "MarketId", "SnapshotAsOfUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityEvidenceSnapshots_Opportunity_AsOf",
                table: "OpportunityEvidenceSnapshots",
                columns: new[] { "OpportunityId", "SnapshotAsOfUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityOfficialConfirmations_SnapshotId",
                table: "OpportunityOfficialConfirmations",
                column: "EvidenceSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityOfficialConfirmations_Source_Time",
                table: "OpportunityOfficialConfirmations",
                columns: new[] { "SourceKey", "ConfirmedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunitySourceObservations_OpportunityId",
                table: "OpportunitySourceObservations",
                column: "OpportunityId");

            migrationBuilder.CreateIndex(
                name: "IX_OpportunitySourceObservations_Source_Time",
                table: "OpportunitySourceObservations",
                columns: new[] { "SourceKey", "ObservedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OpportunitySourceProfiles_SourceKey",
                table: "OpportunitySourceProfiles",
                column: "SourceKey");

            migrationBuilder.CreateIndex(
                name: "IX_OpportunitySourceProfiles_SourceKey_Version",
                table: "OpportunitySourceProfiles",
                columns: new[] { "SourceKey", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpportunityEvidenceCitations");

            migrationBuilder.DropTable(
                name: "OpportunityEvidenceConflicts");

            migrationBuilder.DropTable(
                name: "OpportunityEvidenceSnapshots");

            migrationBuilder.DropTable(
                name: "OpportunityOfficialConfirmations");

            migrationBuilder.DropTable(
                name: "OpportunitySourceObservations");

            migrationBuilder.DropTable(
                name: "OpportunitySourceProfiles");
        }
    }
}
