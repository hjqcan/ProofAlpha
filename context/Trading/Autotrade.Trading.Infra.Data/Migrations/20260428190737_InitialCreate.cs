using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autotrade.Trading.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientOrderId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    StrategyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MarketId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ContextJson = table.Column<string>(type: "jsonb", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RiskEventLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Message = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    StrategyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    MarketId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ContextJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskEventLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TradingAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TotalCapital = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    AvailableCapital = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    AccountUpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradingAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TradingAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Side = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OrderType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TimeInForce = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    GoodTilDateUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NegRisk = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Price = table.Column<decimal>(type: "numeric(18,10)", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,10)", nullable: false),
                    FilledQuantity = table.Column<decimal>(type: "numeric(18,10)", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RejectionReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, nullable: false),
                    StrategyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ClientOrderId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ExchangeOrderId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    OrderSalt = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OrderTimestamp = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    TokenId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Orders_TradingAccounts_TradingAccountId",
                        column: x => x.TradingAccountId,
                        principalTable: "TradingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Positions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TradingAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,10)", nullable: false),
                    AverageCost = table.Column<decimal>(type: "numeric(18,10)", nullable: false),
                    RealizedPnl = table.Column<decimal>(type: "numeric(18,10)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Positions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Positions_TradingAccounts_TradingAccountId",
                        column: x => x.TradingAccountId,
                        principalTable: "TradingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RiskEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TradingAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Message = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ContextJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiskEvents_TradingAccounts_TradingAccountId",
                        column: x => x.TradingAccountId,
                        principalTable: "TradingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Trades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    TradingAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientOrderId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    StrategyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MarketId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TokenId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Side = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,10)", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,10)", nullable: false),
                    ExchangeTradeId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Fee = table.Column<decimal>(type: "numeric(18,10)", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Trades_TradingAccounts_TradingAccountId",
                        column: x => x.TradingAccountId,
                        principalTable: "TradingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderEvents_ClientOrderId",
                table: "OrderEvents",
                column: "ClientOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderEvents_CorrelationId",
                table: "OrderEvents",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderEvents_CreatedAtUtc",
                table: "OrderEvents",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OrderEvents_MarketId",
                table: "OrderEvents",
                column: "MarketId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderEvents_Order_Time",
                table: "OrderEvents",
                columns: new[] { "OrderId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderEvents_OrderId",
                table: "OrderEvents",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderEvents_StrategyId",
                table: "OrderEvents",
                column: "StrategyId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Aggregate_Status_Time",
                table: "Orders",
                columns: new[] { "TradingAccountId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ClientOrderId",
                table: "Orders",
                column: "ClientOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CorrelationId",
                table: "Orders",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CreatedAtUtc",
                table: "Orders",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_MarketId",
                table: "Orders",
                column: "MarketId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Status",
                table: "Orders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StrategyId",
                table: "Orders",
                column: "StrategyId");

            migrationBuilder.CreateIndex(
                name: "UX_Orders_ExchangeOrderId",
                table: "Orders",
                column: "ExchangeOrderId",
                unique: true,
                filter: "\"ExchangeOrderId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Positions_Aggregate_Market_Outcome",
                table: "Positions",
                columns: new[] { "TradingAccountId", "MarketId", "Outcome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiskEventLogs_Code_Time",
                table: "RiskEventLogs",
                columns: new[] { "Code", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RiskEventLogs_Severity_Time",
                table: "RiskEventLogs",
                columns: new[] { "Severity", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RiskEventLogs_Strategy_Time",
                table: "RiskEventLogs",
                columns: new[] { "StrategyId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RiskEvents_Aggregate_Severity_Time",
                table: "RiskEvents",
                columns: new[] { "TradingAccountId", "Severity", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Trades_ClientOrderId",
                table: "Trades",
                column: "ClientOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_CorrelationId",
                table: "Trades",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_CreatedAtUtc",
                table: "Trades",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_Market_Time",
                table: "Trades",
                columns: new[] { "MarketId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Trades_MarketId",
                table: "Trades",
                column: "MarketId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_OrderId",
                table: "Trades",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_Strategy_Time",
                table: "Trades",
                columns: new[] { "StrategyId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Trades_StrategyId",
                table: "Trades",
                column: "StrategyId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_TradingAccountId",
                table: "Trades",
                column: "TradingAccountId");

            migrationBuilder.CreateIndex(
                name: "UX_Trades_ExchangeTradeId",
                table: "Trades",
                column: "ExchangeTradeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradingAccounts_Id",
                table: "TradingAccounts",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "UX_TradingAccounts_WalletAddress",
                table: "TradingAccounts",
                column: "WalletAddress",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderEvents");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "Positions");

            migrationBuilder.DropTable(
                name: "RiskEventLogs");

            migrationBuilder.DropTable(
                name: "RiskEvents");

            migrationBuilder.DropTable(
                name: "Trades");

            migrationBuilder.DropTable(
                name: "TradingAccounts");
        }
    }
}
