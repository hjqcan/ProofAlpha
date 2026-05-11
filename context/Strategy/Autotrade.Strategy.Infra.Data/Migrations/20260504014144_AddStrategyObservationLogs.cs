using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autotrade.Strategy.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStrategyObservationLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StrategyObservationLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StrategyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MarketId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Phase = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FeaturesJson = table.Column<string>(type: "jsonb", nullable: true),
                    StateJson = table.Column<string>(type: "jsonb", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConfigVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExecutionMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyObservationLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StrategyObservationLogs_CorrelationId",
                table: "StrategyObservationLogs",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_StrategyObservationLogs_Strategy_Config_Time",
                table: "StrategyObservationLogs",
                columns: new[] { "StrategyId", "ConfigVersion", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StrategyObservationLogs_Strategy_Reason_Time",
                table: "StrategyObservationLogs",
                columns: new[] { "StrategyId", "ReasonCode", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StrategyObservationLogs_Strategy_Time",
                table: "StrategyObservationLogs",
                columns: new[] { "StrategyId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StrategyObservationLogs");
        }
    }
}
