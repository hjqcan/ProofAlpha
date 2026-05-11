using Autotrade.Testing.Db;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Infra.Data.Repositories;
using Microsoft.Extensions.Options;

namespace Autotrade.Trading.Tests;

public sealed class TradingAccountProvisionerTests
{
    [Fact]
    public async Task EfTradingAccountProvisioner_Provision_ShouldBeIdempotentAndAllowTradeInsert()
    {
        await using var db = new SqliteInMemoryDatabase();
        await using var ctx = TestDbContextFactory.CreateTradingContextSqlite(db.Connection);
        await ctx.Database.EnsureCreatedAsync();

        // Arrange
        var provisioner = new EfTradingAccountProvisioner(ctx);
        var accountKey = "paper";
        var capital = Options.Create(new RiskCapitalOptions
        {
            TotalCapital = 1000m,
            AvailableCapital = 1000m,
            RealizedDailyPnl = 0m
        });

        // Act
        var id1 = await provisioner.ProvisionAsync(accountKey, capital.Value.TotalCapital, capital.Value.AvailableCapital);
        var id2 = await provisioner.ProvisionAsync(accountKey, capital.Value.TotalCapital, capital.Value.AvailableCapital);

        // Assert: stable
        Assert.NotEqual(Guid.Empty, id1);
        Assert.Equal(id1, id2);

        // Assert: can insert trade referencing the aggregate (FK must succeed)
        var tradeRepo = new EfTradeRepository(ctx);
        var tradeDto = new TradeDto(
            Id: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            TradingAccountId: id1,
            ClientOrderId: "client-1",
            StrategyId: "strategy-1",
            MarketId: "market-1",
            TokenId: "token-1",
            Outcome: Domain.Shared.Enums.OutcomeSide.Yes,
            Side: Domain.Shared.Enums.OrderSide.Buy,
            Price: 0.50m,
            Quantity: 10m,
            ExchangeTradeId: "paper-trade-1",
            Fee: 0m,
            CorrelationId: null,
            CreatedAtUtc: DateTimeOffset.UtcNow);

        await tradeRepo.AddAsync(tradeDto);
    }
}

