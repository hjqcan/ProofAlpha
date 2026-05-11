using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autotrade.Strategy.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStrategyParameterVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StrategyParameterVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StrategyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ConfigVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PreviousConfigVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    DiffJson = table.Column<string>(type: "jsonb", nullable: false),
                    ChangeType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Source = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Actor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RollbackSourceVersionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyParameterVersions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StrategyParameterVersions_Strategy_ConfigVersion",
                table: "StrategyParameterVersions",
                columns: new[] { "StrategyId", "ConfigVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StrategyParameterVersions_Strategy_Time",
                table: "StrategyParameterVersions",
                columns: new[] { "StrategyId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StrategyParameterVersions");
        }
    }
}
