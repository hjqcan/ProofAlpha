using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autotrade.Strategy.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommandAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommandName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ArgumentsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Actor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    ExitCode = table.Column<int>(type: "integer", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StrategyDecisionLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StrategyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    MarketId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ContextJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfigVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ExecutionMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyDecisionLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StrategyRunStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StrategyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ConfigVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RestartCount = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastDecisionAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastHeartbeatAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ActiveMarketsJson = table.Column<string>(type: "jsonb", nullable: true),
                    CycleCount = table.Column<long>(type: "bigint", nullable: false),
                    SnapshotsProcessed = table.Column<long>(type: "bigint", nullable: false),
                    ChannelBacklog = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyRunStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommandAuditLogs_CreatedAt",
                table: "CommandAuditLogs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StrategyDecisionLogs_CorrelationId",
                table: "StrategyDecisionLogs",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_StrategyDecisionLogs_Strategy_Time",
                table: "StrategyDecisionLogs",
                columns: new[] { "StrategyId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StrategyRunStates_StrategyId",
                table: "StrategyRunStates",
                column: "StrategyId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommandAuditLogs");

            migrationBuilder.DropTable(
                name: "StrategyDecisionLogs");

            migrationBuilder.DropTable(
                name: "StrategyRunStates");
        }
    }
}
