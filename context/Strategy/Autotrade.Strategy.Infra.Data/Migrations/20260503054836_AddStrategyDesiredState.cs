using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autotrade.Strategy.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStrategyDesiredState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DesiredState",
                table: "StrategyRunStates",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Running");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DesiredState",
                table: "StrategyRunStates");
        }
    }
}
