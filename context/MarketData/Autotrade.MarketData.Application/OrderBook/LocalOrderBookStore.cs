using System.Collections.Concurrent;
using Autotrade.MarketData.Application.Contract.OrderBook;
using Autotrade.MarketData.Application.Contract.WebSocket.Events;
using Autotrade.MarketData.Domain.Shared.ValueObjects;

namespace Autotrade.MarketData.Application.OrderBook;

/// <summary>
/// 本地订单簿存储，提供线程安全的内存订单簿管理。
/// 支持快照更新、增量更新、深度查询等功能。
/// </summary>
public sealed class LocalOrderBookStore : ILocalOrderBookStore, IOrderBookReader
{
    private readonly ConcurrentDictionary<string, OrderBookState> _orderBooks = new();

    /// <inheritdoc />
    public IReadOnlyCollection<string> TrackedAssets => _orderBooks.Keys.ToList();

    /// <inheritdoc />
    public void ApplySnapshot(ClobBookEvent bookEvent)
    {
        ArgumentNullException.ThrowIfNull(bookEvent);

        var state = new OrderBookState(bookEvent.AssetId, bookEvent.Market, bookEvent.Hash);

        // 应用买单
        foreach (var bid in bookEvent.Bids)
        {
            state.Bids[bid.PriceDecimal] = bid.SizeDecimal;
        }

        // 应用卖单
        foreach (var ask in bookEvent.Asks)
        {
            state.Asks[ask.PriceDecimal] = ask.SizeDecimal;
        }

        state.LastUpdatedUtc = DateTimeOffset.UtcNow;
        state.SnapshotTimestamp = bookEvent.Timestamp;

        _orderBooks[bookEvent.AssetId] = state;
    }

    /// <inheritdoc />
    public void ApplyDelta(ClobPriceChangeEvent priceChangeEvent)
    {
        ArgumentNullException.ThrowIfNull(priceChangeEvent);

        foreach (var change in priceChangeEvent.PriceChanges)
        {
            if (!_orderBooks.TryGetValue(change.AssetId, out var state))
            {
                // 没有快照，忽略增量
                continue;
            }

            var price = change.PriceDecimal;
            var size = change.SizeDecimal;

            if (change.IsBuy)
            {
                if (size <= 0)
                {
                    state.Bids.TryRemove(price, out _);
                }
                else
                {
                    state.Bids[price] = size;
                }
            }
            else
            {
                if (size <= 0)
                {
                    state.Asks.TryRemove(price, out _);
                }
                else
                {
                    state.Asks[price] = size;
                }
            }

            state.LastUpdatedUtc = DateTimeOffset.UtcNow;
        }
    }

    /// <inheritdoc />
    public OrderBookSnapshot? GetSnapshot(string assetId)
    {
        if (!_orderBooks.TryGetValue(assetId, out var state))
        {
            return null;
        }

        return state.ToSnapshot();
    }

    /// <inheritdoc />
    public TopOfBook? GetTopOfBook(string assetId)
    {
        if (!_orderBooks.TryGetValue(assetId, out var state))
        {
            return null;
        }

        var bestBid = state.Bids.Keys.OrderByDescending(p => p).FirstOrDefault();
        var bestAsk = state.Asks.Keys.OrderBy(p => p).FirstOrDefault();

        return new TopOfBook(
            AssetId: assetId,
            BestBidPrice: bestBid > 0 ? new Price(bestBid) : null,
            BestBidSize: bestBid > 0 && state.Bids.TryGetValue(bestBid, out var bidSize) ? new Quantity(bidSize) : null,
            BestAskPrice: bestAsk > 0 ? new Price(bestAsk) : null,
            BestAskSize: bestAsk > 0 && state.Asks.TryGetValue(bestAsk, out var askSize) ? new Quantity(askSize) : null,
            Spread: bestBid > 0 && bestAsk > 0 ? bestAsk - bestBid : null,
            LastUpdatedUtc: state.LastUpdatedUtc);
    }

    /// <inheritdoc />
    public IReadOnlyList<PriceLevel> GetDepth(string assetId, int levels = 10)
    {
        if (!_orderBooks.TryGetValue(assetId, out var state))
        {
            return Array.Empty<PriceLevel>();
        }

        var bids = state.Bids
            .OrderByDescending(kv => kv.Key)
            .Take(levels)
            .Select(kv => new PriceLevel(kv.Key, kv.Value, true));

        var asks = state.Asks
            .OrderBy(kv => kv.Key)
            .Take(levels)
            .Select(kv => new PriceLevel(kv.Key, kv.Value, false));

        return bids.Concat(asks).ToList();
    }

    /// <inheritdoc />
    public void Clear(string assetId)
    {
        _orderBooks.TryRemove(assetId, out _);
    }

    /// <inheritdoc />
    public void ClearAll()
    {
        _orderBooks.Clear();
    }

    // IOrderBookReader 显式实现
    TopOfBookDto? IOrderBookReader.GetTopOfBook(string assetId)
    {
        var topOfBook = GetTopOfBook(assetId);
        if (topOfBook is null) return null;

        return new TopOfBookDto(
            topOfBook.AssetId,
            topOfBook.BestBidPrice,
            topOfBook.BestBidSize,
            topOfBook.BestAskPrice,
            topOfBook.BestAskSize,
            topOfBook.Spread,
            topOfBook.LastUpdatedUtc);
    }

    IReadOnlyList<PriceLevelDto> IOrderBookReader.GetDepth(string assetId, int levels)
    {
        return GetDepth(assetId, levels)
            .Select(p => new PriceLevelDto(p.Price, p.Size, p.IsBid))
            .ToList();
    }

    /// <summary>
    /// 内部订单簿状态。
    /// </summary>
    private sealed class OrderBookState
    {
        public string AssetId { get; }
        public string Market { get; }
        public string Hash { get; set; }

        /// <summary>
        /// 买单（价格 -> 数量），价格降序。
        /// </summary>
        public ConcurrentDictionary<decimal, decimal> Bids { get; } = new();

        /// <summary>
        /// 卖单（价格 -> 数量），价格升序。
        /// </summary>
        public ConcurrentDictionary<decimal, decimal> Asks { get; } = new();

        public DateTimeOffset LastUpdatedUtc { get; set; }
        public string SnapshotTimestamp { get; set; } = string.Empty;

        public OrderBookState(string assetId, string market, string hash)
        {
            AssetId = assetId;
            Market = market;
            Hash = hash;
        }

        public OrderBookSnapshot ToSnapshot()
        {
            var bids = Bids
                .OrderByDescending(kv => kv.Key)
                .Select(kv => new PriceLevel(kv.Key, kv.Value, true))
                .ToList();

            var asks = Asks
                .OrderBy(kv => kv.Key)
                .Select(kv => new PriceLevel(kv.Key, kv.Value, false))
                .ToList();

            return new OrderBookSnapshot(AssetId, Market, bids, asks, LastUpdatedUtc);
        }
    }
}

/// <summary>
/// 订单簿快照。
/// </summary>
public sealed record OrderBookSnapshot(
    string AssetId,
    string Market,
    IReadOnlyList<PriceLevel> Bids,
    IReadOnlyList<PriceLevel> Asks,
    DateTimeOffset LastUpdatedUtc);

/// <summary>
/// 最优买卖价。
/// </summary>
public sealed record TopOfBook(
    string AssetId,
    Price? BestBidPrice,
    Quantity? BestBidSize,
    Price? BestAskPrice,
    Quantity? BestAskSize,
    decimal? Spread,
    DateTimeOffset LastUpdatedUtc);

/// <summary>
/// 价格档位。
/// </summary>
public sealed record PriceLevel(decimal Price, decimal Size, bool IsBid);
