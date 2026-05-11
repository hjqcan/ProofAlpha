using Autotrade.Trading.Application.Execution;
using Microsoft.Extensions.Logging.Abstractions;

namespace Autotrade.Trading.Tests.Execution;

public class InMemoryIdempotencyStoreTests
{
    private readonly InMemoryIdempotencyStore _store;

    public InMemoryIdempotencyStoreTests()
    {
        _store = new InMemoryIdempotencyStore(NullLogger<InMemoryIdempotencyStore>.Instance);
    }

    [Fact]
    public async Task TryAddAsync_新条目_应返回IsNew为True()
    {
        var (isNew, existingId) = await _store.TryAddAsync(
            "order-001",
            "hash-001",
            TimeSpan.FromMinutes(5));

        Assert.True(isNew);
        Assert.Null(existingId);
    }

    [Fact]
    public async Task TryAddAsync_重复请求相同哈希_应返回IsNew为False()
    {
        await _store.TryAddAsync("order-001", "hash-001", TimeSpan.FromMinutes(5));

        var (isNew, existingId) = await _store.TryAddAsync(
            "order-001",
            "hash-001",
            TimeSpan.FromMinutes(5));

        Assert.False(isNew);
        Assert.Null(existingId); // 尚未设置 ExchangeOrderId
    }

    [Fact]
    public async Task TryAddAsync_重复请求不同哈希_应抛出异常()
    {
        await _store.TryAddAsync("order-001", "hash-001", TimeSpan.FromMinutes(5));

        await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            _store.TryAddAsync("order-001", "hash-002", TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task SetExchangeOrderIdAsync_应正确设置()
    {
        await _store.TryAddAsync("order-001", "hash-001", TimeSpan.FromMinutes(5));
        await _store.SetExchangeOrderIdAsync("order-001", "exchange-001");

        var entry = await _store.GetAsync("order-001");

        Assert.NotNull(entry);
        Assert.Equal("exchange-001", entry.ExchangeOrderId);
    }

    [Fact]
    public async Task SetMarketInfoAsync_应正确设置()
    {
        await _store.TryAddAsync("order-001", "hash-001", TimeSpan.FromMinutes(5));
        await _store.SetMarketInfoAsync("order-001", "market-001", "token-001");

        var entry = await _store.GetAsync("order-001");

        Assert.NotNull(entry);
        Assert.Equal("market-001", entry.MarketId);
        Assert.Equal("token-001", entry.TokenId);
    }

    [Fact]
    public async Task TryAddAsync_重复请求有ExchangeOrderId_应返回该ID()
    {
        await _store.TryAddAsync("order-001", "hash-001", TimeSpan.FromMinutes(5));
        await _store.SetExchangeOrderIdAsync("order-001", "exchange-001");

        var (isNew, existingId) = await _store.TryAddAsync(
            "order-001",
            "hash-001",
            TimeSpan.FromMinutes(5));

        Assert.False(isNew);
        Assert.Equal("exchange-001", existingId);
    }

    [Fact]
    public async Task GetAsync_不存在的条目_应返回Null()
    {
        var entry = await _store.GetAsync("nonexistent");
        Assert.Null(entry);
    }

    [Fact]
    public async Task GetAsync_过期条目_应返回Null()
    {
        await _store.TryAddAsync("order-001", "hash-001", TimeSpan.FromMilliseconds(1));

        // 等待过期
        await Task.Delay(10);

        var entry = await _store.GetAsync("order-001");
        Assert.Null(entry);
    }

    [Fact]
    public async Task FindClientOrderIdByExchangeIdAsync_存在映射_应返回ClientOrderId()
    {
        await _store.TryAddAsync("order-001", "hash-001", TimeSpan.FromMinutes(5));
        await _store.SetExchangeOrderIdAsync("order-001", "exchange-001");

        var clientOrderId = await _store.FindClientOrderIdByExchangeIdAsync("exchange-001");

        Assert.Equal("order-001", clientOrderId);
    }

    [Fact]
    public async Task FindClientOrderIdByExchangeIdAsync_不存在映射_应返回Null()
    {
        var clientOrderId = await _store.FindClientOrderIdByExchangeIdAsync("nonexistent");
        Assert.Null(clientOrderId);
    }

    [Fact]
    public async Task 并发添加_应正确处理()
    {
        var tasks = Enumerable.Range(0, 10).Select(i =>
            _store.TryAddAsync($"order-{i:D3}", $"hash-{i:D3}", TimeSpan.FromMinutes(5)));

        await Task.WhenAll(tasks);

        for (int i = 0; i < 10; i++)
        {
            var entry = await _store.GetAsync($"order-{i:D3}");
            Assert.NotNull(entry);
        }
    }
    [Fact]
    public async Task SeedAsync_RestoredEntry_ShouldRebuildExchangeIdMapping()
    {
        await _store.SeedAsync(new OrderTrackingEntry
        {
            ClientOrderId = "order-restore",
            ExchangeOrderId = "exchange-restore",
            MarketId = "market-001",
            TokenId = "token-001",
            StrategyId = "strategy-001",
            CorrelationId = "corr-001",
            RequestHash = "restored:order-restore",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        });

        var entry = await _store.GetAsync("order-restore");
        var clientOrderId = await _store.FindClientOrderIdByExchangeIdAsync("exchange-restore");

        Assert.NotNull(entry);
        Assert.Equal("exchange-restore", entry.ExchangeOrderId);
        Assert.Equal("market-001", entry.MarketId);
        Assert.Equal("order-restore", clientOrderId);
    }
}
