using Autotrade.MarketData.Domain.Entities;
using Autotrade.MarketData.Domain.Shared.Enums;
using Autotrade.MarketData.Domain.Shared.ValueObjects;
using Autotrade.Testing.Db;
using Microsoft.EntityFrameworkCore;

namespace Autotrade.MarketData.Tests;

public class MarketDomainTests
{
    [Fact]
    public void Market_创建参数非法_应抛异常()
    {
        Assert.Throws<ArgumentException>(() => new Market(marketId: " ", name: "ok"));
        Assert.Throws<ArgumentException>(() => new Market(marketId: "m1", name: " "));
    }

    [Fact]
    public void Market_Rename_应更新名称()
    {
        var m = new Market("m1", "old");
        m.Rename("new");
        Assert.Equal("new", m.Name);
    }

    [Fact]
    public void Market_UpdateStatus_应更新状态()
    {
        var m = new Market("m1", "name");
        m.UpdateStatus(MarketStatus.Suspended);
        Assert.Equal(MarketStatus.Suspended, m.Status);
    }
}

public class OrderBookDomainTests
{
    [Fact]
    public void OrderBook_创建参数非法_应抛异常()
    {
        Assert.Throws<ArgumentException>(() => new OrderBook(" "));
    }

    [Fact]
    public void OrderBook_UpdateTopOfBook_应更新最优买卖与时间()
    {
        var ob = new OrderBook("m1");
        var ts = DateTimeOffset.UtcNow.AddSeconds(-1);

        ob.UpdateTopOfBook(
            bestBidPrice: new Price(0.4m),
            bestBidSize: new Quantity(10m),
            bestAskPrice: new Price(0.6m),
            bestAskSize: new Quantity(20m),
            updatedAtUtc: ts);

        Assert.Equal(0.4m, ob.BestBidPrice!.Value);
        Assert.Equal(10m, ob.BestBidSize!.Value);
        Assert.Equal(0.6m, ob.BestAskPrice!.Value);
        Assert.Equal(20m, ob.BestAskSize!.Value);
        Assert.Equal(ts, ob.LastUpdatedAtUtc);
    }
}

public class MarketDataContextRoundTripTests
{
    [Fact]
    public async Task Sqlite_RoundTrip_应持久化Market并可回读()
    {
        await using var db = new SqliteInMemoryDatabase();
        await using var ctx = TestDbContextFactory.CreateMarketDataContextSqlite(db.Connection);

        await ctx.Database.EnsureCreatedAsync();

        var market = new Market("m1", "test", DateTimeOffset.UtcNow.AddMinutes(15));
        ctx.Markets.Add(market);
        await ctx.SaveChangesAsync();

        var loaded = await ctx.Markets.SingleAsync(x => x.MarketId == "m1");
        Assert.Equal("test", loaded.Name);
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

        await using var ctx = TestDbContextFactory.CreateMarketDataContextPostgres(cs);
        await ctx.Database.EnsureCreatedAsync();

        // 简单 CRUD 验证
        ctx.Markets.Add(new Market("m-smoke", "smoke"));
        await ctx.SaveChangesAsync();

        var loaded = await ctx.Markets.SingleAsync(x => x.MarketId == "m-smoke");
        Assert.Equal("smoke", loaded.Name);
    }
}
