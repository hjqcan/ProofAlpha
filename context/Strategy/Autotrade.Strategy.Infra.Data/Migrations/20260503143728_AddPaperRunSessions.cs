using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autotrade.Strategy.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaperRunSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RunSessionId",
                table: "StrategyDecisionLogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PaperRunSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutionMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConfigVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StrategiesJson = table.Column<string>(type: "jsonb", nullable: false),
                    RiskProfileJson = table.Column<string>(type: "jsonb", nullable: false),
                    OperatorSource = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StoppedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StopReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperRunSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StrategyDecisionLogs_RunSessionId",
                table: "StrategyDecisionLogs",
                column: "RunSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_PaperRunSessions_Mode_Stop",
                table: "PaperRunSessions",
                columns: new[] { "ExecutionMode", "StoppedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PaperRunSessions_StartedAt",
                table: "PaperRunSessions",
                column: "StartedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaperRunSessions");

            migrationBuilder.DropIndex(
                name: "IX_StrategyDecisionLogs_RunSessionId",
                table: "StrategyDecisionLogs");

            migrationBuilder.DropColumn(
                name: "RunSessionId",
                table: "StrategyDecisionLogs");
        }
    }
}
