using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autotrade.Trading.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderEventRunSessionId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RunSessionId",
                table: "OrderEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderEvents_RunSessionId",
                table: "OrderEvents",
                column: "RunSessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderEvents_RunSessionId",
                table: "OrderEvents");

            migrationBuilder.DropColumn(
                name: "RunSessionId",
                table: "OrderEvents");
        }
    }
}
