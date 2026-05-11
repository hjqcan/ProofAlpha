using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autotrade.Strategy.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStrategyBlockedReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BlockedReasonCode",
                table: "StrategyRunStates",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlockedReasonKind",
                table: "StrategyRunStates",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlockedReasonMessage",
                table: "StrategyRunStates",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlockedReasonCode",
                table: "StrategyRunStates");

            migrationBuilder.DropColumn(
                name: "BlockedReasonKind",
                table: "StrategyRunStates");

            migrationBuilder.DropColumn(
                name: "BlockedReasonMessage",
                table: "StrategyRunStates");
        }
    }
}
