using Autotrade.Testing.Builders;
using Autotrade.Testing.Db;
using Autotrade.Trading.Domain.Entities;
using Autotrade.Trading.Domain.Shared.Enums;
using Autotrade.Trading.Domain.Shared.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Autotrade.Trading.Tests;

public class TradingContextRoundTripTests
{
    [Fact]
    public async Task Sqlite_RoundTrip_应持久化聚合图并可回读()
    {
        await using var db = new SqliteInMemoryDatabase();
        await using var ctx = TestDbContextFactory.CreateTradingContextSqlite(db.Connection);

        await ctx.Database.EnsureCreatedAsync();

        var account = TradingTestData.NewTradingAccount(walletAddress: "w", totalCapital: 100m, availableCapital: 100m);
        var order = new Order(
            tradingAccountId: account.Id,
            marketId: "m1",
            outcome: OutcomeSide.Yes,
            side: OrderSide.Buy,
            orderType: OrderType.Limit,
            timeInForce: TimeInForce.Gtc,
            price: new Price(0.4m),
            quantity: new Quantity(10m));

        var position = new Position(account.Id, "m1", OutcomeSide.Yes);
        position.ApplyBuy(new Quantity(10m), new Price(0.4m));

        account.Orders.Add(order);
        account.Positions.Add(position);
        account.RecordRiskEvent("TEST", RiskSeverity.Info, "ok");

        ctx.TradingAccounts.Add(account);
        await ctx.SaveChangesAsync();

        var loaded = await ctx.TradingAccounts
            .Include(x => x.Orders)
            .Include(x => x.Positions)
            .Include(x => x.RiskEvents)
            .SingleAsync(x => x.Id == account.Id);

        Assert.Equal("w", loaded.WalletAddress);
        Assert.Single(loaded.Orders);
        Assert.Single(loaded.Positions);
        Assert.Single(loaded.RiskEvents);
    }

    [Fact]
    public async Task Postgres_MigrateSmoke_若未配置连接串则跳过()
    {
        var cs = Environment.GetEnvironmentVariable("AUTOTRADE_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(cs))
        {
            // xUnit v2 不支持运行时 Skip（会导致编译/运行问题），这里选择直接返回。
            // 想执行该用例：设置 AUTOTRADE_TEST_POSTGRES（指向测试库），再运行 dotnet test。
            return;
        }

        await using var ctx = TestDbContextFactory.CreateTradingContextPostgres(cs);
        await ctx.Database.EnsureCreatedAsync();

        ctx.TradingAccounts.Add(new TradingAccount(walletAddress: "w", totalCapital: 1m, availableCapital: 1m));
        await ctx.SaveChangesAsync();
    }
}

