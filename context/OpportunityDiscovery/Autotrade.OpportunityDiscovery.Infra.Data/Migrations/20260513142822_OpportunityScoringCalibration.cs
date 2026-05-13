using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autotrade.OpportunityDiscovery.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class OpportunityScoringCalibration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanPromote",
                table: "OpportunityScores",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "ExecutableCapacity",
                table: "OpportunityScores",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExecutableEntryPrice",
                table: "OpportunityScores",
                type: "numeric(8,4)",
                precision: 8,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FeeEstimate",
                table: "OpportunityScores",
                type: "numeric(8,4)",
                precision: 8,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LlmFairProbability",
                table: "OpportunityScores",
                type: "numeric(8,4)",
                precision: 8,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MarketImpliedProbability",
                table: "OpportunityScores",
                type: "numeric(8,4)",
                precision: 8,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "NetEdge",
                table: "OpportunityScores",
                type: "numeric(8,4)",
                precision: 8,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SlippageBuffer",
                table: "OpportunityScores",
                type: "numeric(8,4)",
                precision: 8,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityScores_CanPromote_NetEdge",
                table: "OpportunityScores",
                columns: new[] { "CanPromote", "NetEdge" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OpportunityScores_CanPromote_NetEdge",
                table: "OpportunityScores");

            migrationBuilder.DropColumn(
                name: "CanPromote",
                table: "OpportunityScores");

            migrationBuilder.DropColumn(
                name: "ExecutableCapacity",
                table: "OpportunityScores");

            migrationBuilder.DropColumn(
                name: "ExecutableEntryPrice",
                table: "OpportunityScores");

            migrationBuilder.DropColumn(
                name: "FeeEstimate",
                table: "OpportunityScores");

            migrationBuilder.DropColumn(
                name: "LlmFairProbability",
                table: "OpportunityScores");

            migrationBuilder.DropColumn(
                name: "MarketImpliedProbability",
                table: "OpportunityScores");

            migrationBuilder.DropColumn(
                name: "NetEdge",
                table: "OpportunityScores");

            migrationBuilder.DropColumn(
                name: "SlippageBuffer",
                table: "OpportunityScores");
        }
    }
}
