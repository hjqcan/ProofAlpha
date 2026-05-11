using Autotrade.MarketData.Application.Contract.WebSocket.Events;
using Autotrade.MarketData.Application.OrderBook;

namespace Autotrade.MarketData.Tests;

/// <summary>
/// LocalOrderBookStore 单元测试。
/// 验证快照/增量更新、一致性校验等核心逻辑。
/// </summary>
public class OrderBookStoreTests
{
    [Fact]
    public void ApplySnapshot_应正确初始化订单簿()
    {
        var store = new LocalOrderBookStore();

        var snapshot = new ClobBookEvent
        {
            EventType = "book",
            AssetId = "asset-1",
            Market = "market-1",
            Timestamp = "1704355200000",
            Hash = "abc123",
            Bids = new List<OrderLevel>
            {
                new() { Price = "0.50", Size = "100" },
                new() { Price = "0.49", Size = "200" }
            },
            Asks = new List<OrderLevel>
            {
                new() { Price = "0.51", Size = "150" },
                new() { Price = "0.52", Size = "250" }
            }
        };

        store.ApplySnapshot(snapshot);

        var topOfBook = store.GetTopOfBook("asset-1");
        Assert.NotNull(topOfBook);
        Assert.Equal(0.50m, topOfBook.BestBidPrice!.Value);
        Assert.Equal(100m, topOfBook.BestBidSize!.Value);
        Assert.Equal(0.51m, topOfBook.BestAskPrice!.Value);
        Assert.Equal(150m, topOfBook.BestAskSize!.Value);
        Assert.Equal(0.01m, topOfBook.Spread);
    }

    [Fact]
    public void ApplyDelta_添加新价格层级_应正确更新()
    {
        var store = new LocalOrderBookStore();

        // 先应用快照
        store.ApplySnapshot(CreateBasicSnapshot("asset-1"));

        // 添加新的买单价格层级
        var delta = new ClobPriceChangeEvent
        {
            EventType = "price_change",
            Market = "market-1",
            Timestamp = "1704355201000",
            PriceChanges = new List<PriceChange>
            {
                new()
                {
                    AssetId = "asset-1",
                    Price = "0.48",
                    Size = "300",
                    Side = "BUY",
                    Hash = "def456",
                    BestBid = "0.50",
                    BestAsk = "0.51"
                }
            }
        };

        store.ApplyDelta(delta);

        var depth = store.GetDepth("asset-1", 10);
        Assert.Contains(depth, l => l.Price == 0.48m && l.Size == 300m && l.IsBid);
    }

    [Fact]
    public void ApplyDelta_Size为零_应移除价格层级()
    {
        var store = new LocalOrderBookStore();
        store.ApplySnapshot(CreateBasicSnapshot("asset-1"));

        // 移除最优买价
        var delta = new ClobPriceChangeEvent
        {
            EventType = "price_change",
            Market = "market-1",
            Timestamp = "1704355201000",
            PriceChanges = new List<PriceChange>
            {
                new()
                {
                    AssetId = "asset-1",
                    Price = "0.50",
                    Size = "0",
                    Side = "BUY",
                    Hash = "def456",
                    BestBid = "0.49",
                    BestAsk = "0.51"
                }
            }
        };

        store.ApplyDelta(delta);

        var topOfBook = store.GetTopOfBook("asset-1");
        Assert.NotNull(topOfBook);
        // 最优买价应该变为 0.49
        Assert.Equal(0.49m, topOfBook.BestBidPrice!.Value);
    }

    [Fact]
    public void ApplyDelta_无快照时_应忽略增量()
    {
        var store = new LocalOrderBookStore();

        var delta = new ClobPriceChangeEvent
        {
            EventType = "price_change",
            Market = "market-1",
            Timestamp = "1704355201000",
            PriceChanges = new List<PriceChange>
            {
                new()
                {
                    AssetId = "asset-no-snapshot",
                    Price = "0.50",
                    Size = "100",
                    Side = "BUY",
                    Hash = "def456",
                    BestBid = "0.50",
                    BestAsk = "0.51"
                }
            }
        };

        store.ApplyDelta(delta);

        var topOfBook = store.GetTopOfBook("asset-no-snapshot");
        Assert.Null(topOfBook);
    }

    [Fact]
    public void Clear_应清除指定资产的订单簿()
    {
        var store = new LocalOrderBookStore();
        store.ApplySnapshot(CreateBasicSnapshot("asset-1"));
        store.ApplySnapshot(CreateBasicSnapshot("asset-2"));

        Assert.Contains("asset-1", store.TrackedAssets);
        Assert.Contains("asset-2", store.TrackedAssets);

        store.Clear("asset-1");

        Assert.DoesNotContain("asset-1", store.TrackedAssets);
        Assert.Contains("asset-2", store.TrackedAssets);
    }

    [Fact]
    public void ClearAll_应清除所有订单簿()
    {
        var store = new LocalOrderBookStore();
        store.ApplySnapshot(CreateBasicSnapshot("asset-1"));
        store.ApplySnapshot(CreateBasicSnapshot("asset-2"));

        store.ClearAll();

        Assert.Empty(store.TrackedAssets);
    }

    [Fact]
    public void GetDepth_应返回正确排序的深度()
    {
        var store = new LocalOrderBookStore();

        var snapshot = new ClobBookEvent
        {
            EventType = "book",
            AssetId = "asset-1",
            Market = "market-1",
            Timestamp = "1704355200000",
            Hash = "abc123",
            Bids = new List<OrderLevel>
            {
                new() { Price = "0.48", Size = "100" },
                new() { Price = "0.50", Size = "200" },
                new() { Price = "0.49", Size = "150" }
            },
            Asks = new List<OrderLevel>
            {
                new() { Price = "0.53", Size = "250" },
                new() { Price = "0.51", Size = "175" },
                new() { Price = "0.52", Size = "225" }
            }
        };

        store.ApplySnapshot(snapshot);

        var depth = store.GetDepth("asset-1", 3).ToList();

        // Bids 应按价格降序排列
        var bids = depth.Where(l => l.IsBid).ToList();
        Assert.Equal(0.50m, bids[0].Price);
        Assert.Equal(0.49m, bids[1].Price);
        Assert.Equal(0.48m, bids[2].Price);

        // Asks 应按价格升序排列
        var asks = depth.Where(l => !l.IsBid).ToList();
        Assert.Equal(0.51m, asks[0].Price);
        Assert.Equal(0.52m, asks[1].Price);
        Assert.Equal(0.53m, asks[2].Price);
    }

    [Fact]
    public void GetSnapshot_应返回完整快照()
    {
        var store = new LocalOrderBookStore();
        store.ApplySnapshot(CreateBasicSnapshot("asset-1"));

        var snapshot = store.GetSnapshot("asset-1");

        Assert.NotNull(snapshot);
        Assert.Equal("asset-1", snapshot.AssetId);
        Assert.Equal(2, snapshot.Bids.Count);
        Assert.Equal(2, snapshot.Asks.Count);
    }

    [Fact]
    public void 多资产并发更新_应线程安全()
    {
        var store = new LocalOrderBookStore();

        // 并行创建多个资产的订单簿
        Parallel.For(0, 100, i =>
        {
            var assetId = $"asset-{i}";
            store.ApplySnapshot(CreateBasicSnapshot(assetId));

            // 多次增量更新
            for (var j = 0; j < 10; j++)
            {
                var delta = new ClobPriceChangeEvent
                {
                    EventType = "price_change",
                    Market = $"market-{i}",
                    Timestamp = $"{1704355200000 + j}",
                    PriceChanges = new List<PriceChange>
                    {
                        new()
                        {
                            AssetId = assetId,
                            Price = $"0.{45 + j}",
                            Size = $"{100 + j * 10}",
                            Side = j % 2 == 0 ? "BUY" : "SELL",
                            Hash = $"hash-{i}-{j}",
                            BestBid = "0.50",
                            BestAsk = "0.51"
                        }
                    }
                };
                store.ApplyDelta(delta);
            }
        });

        Assert.Equal(100, store.TrackedAssets.Count);
    }

    private static ClobBookEvent CreateBasicSnapshot(string assetId)
    {
        return new ClobBookEvent
        {
            EventType = "book",
            AssetId = assetId,
            Market = $"market-{assetId}",
            Timestamp = "1704355200000",
            Hash = $"hash-{assetId}",
            Bids = new List<OrderLevel>
            {
                new() { Price = "0.50", Size = "100" },
                new() { Price = "0.49", Size = "200" }
            },
            Asks = new List<OrderLevel>
            {
                new() { Price = "0.51", Size = "150" },
                new() { Price = "0.52", Size = "250" }
            }
        };
    }
}
