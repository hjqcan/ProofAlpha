using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autotrade.MarketData.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketDataTape : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClobTradeTicks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TokenId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExchangeTradeId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TimestampUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TimestampUnixMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    Size = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Side = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FeeRateBps = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    SourceName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RawJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClobTradeTicks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MarketPriceTicks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TokenId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TimestampUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TimestampUnixMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    Size = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: true),
                    SourceName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceSequence = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    RawJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketPriceTicks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MarketResolutionEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedUnixMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RawJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketResolutionEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrderBookDepthSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TokenId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TimestampUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TimestampUnixMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    SnapshotHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    BidsJson = table.Column<string>(type: "jsonb", nullable: false),
                    AsksJson = table.Column<string>(type: "jsonb", nullable: false),
                    SourceName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RawJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderBookDepthSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrderBookTopTicks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TokenId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TimestampUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TimestampUnixMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    BestBidPrice = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    BestBidSize = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: true),
                    BestAskPrice = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    BestAskSize = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: true),
                    Spread = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    SourceName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceSequence = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    RawJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderBookTopTicks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClobTradeTicks_Dedup",
                table: "ClobTradeTicks",
                columns: new[] { "MarketId", "TokenId", "ExchangeTradeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClobTradeTicks_Market_Token_Time",
                table: "ClobTradeTicks",
                columns: new[] { "MarketId", "TokenId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClobTradeTicks_Market_Token_UnixTime",
                table: "ClobTradeTicks",
                columns: new[] { "MarketId", "TokenId", "TimestampUnixMilliseconds" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketPriceTicks_Market_Token_Source_Sequence",
                table: "MarketPriceTicks",
                columns: new[] { "MarketId", "TokenId", "SourceName", "SourceSequence" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketPriceTicks_Market_Token_Time",
                table: "MarketPriceTicks",
                columns: new[] { "MarketId", "TokenId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketPriceTicks_Market_Token_UnixTime",
                table: "MarketPriceTicks",
                columns: new[] { "MarketId", "TokenId", "TimestampUnixMilliseconds" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketResolutionEvents_Market_ResolvedAt",
                table: "MarketResolutionEvents",
                columns: new[] { "MarketId", "ResolvedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketResolutionEvents_Market_ResolvedUnix",
                table: "MarketResolutionEvents",
                columns: new[] { "MarketId", "ResolvedUnixMilliseconds" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderBookDepthSnapshots_Dedup",
                table: "OrderBookDepthSnapshots",
                columns: new[] { "MarketId", "TokenId", "SnapshotHash", "TimestampUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderBookDepthSnapshots_Market_Token_Time",
                table: "OrderBookDepthSnapshots",
                columns: new[] { "MarketId", "TokenId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderBookDepthSnapshots_Market_Token_UnixTime",
                table: "OrderBookDepthSnapshots",
                columns: new[] { "MarketId", "TokenId", "TimestampUnixMilliseconds" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderBookTopTicks_Market_Token_Source_Sequence",
                table: "OrderBookTopTicks",
                columns: new[] { "MarketId", "TokenId", "SourceName", "SourceSequence" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderBookTopTicks_Market_Token_Time",
                table: "OrderBookTopTicks",
                columns: new[] { "MarketId", "TokenId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderBookTopTicks_Market_Token_UnixTime",
                table: "OrderBookTopTicks",
                columns: new[] { "MarketId", "TokenId", "TimestampUnixMilliseconds" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClobTradeTicks");

            migrationBuilder.DropTable(
                name: "MarketPriceTicks");

            migrationBuilder.DropTable(
                name: "MarketResolutionEvents");

            migrationBuilder.DropTable(
                name: "OrderBookDepthSnapshots");

            migrationBuilder.DropTable(
                name: "OrderBookTopTicks");
        }
    }
}
