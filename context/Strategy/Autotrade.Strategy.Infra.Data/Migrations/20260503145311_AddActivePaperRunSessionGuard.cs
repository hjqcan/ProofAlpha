using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autotrade.Strategy.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddActivePaperRunSessionGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PaperRunSessions_ActiveMode",
                table: "PaperRunSessions",
                column: "ExecutionMode",
                unique: true,
                filter: "\"StoppedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaperRunSessions_ActiveMode",
                table: "PaperRunSessions");
        }
    }
}
