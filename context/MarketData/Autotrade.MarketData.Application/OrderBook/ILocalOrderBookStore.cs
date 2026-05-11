using System.Collections.Concurrent;
using Autotrade.MarketData.Application.Contract.OrderBook;
using Autotrade.MarketData.Application.Contract.WebSocket.Events;
using Autotrade.MarketData.Domain.Shared.ValueObjects;

namespace Autotrade.MarketData.Application.OrderBook;

/// <summary>
/// 本地订单簿存储接口。
/// </summary>
public interface ILocalOrderBookStore
{
    /// <summary>
    /// 当前跟踪的资产 ID 列表。
    /// </summary>
    IReadOnlyCollection<string> TrackedAssets { get; }

    /// <summary>
    /// 应用订单簿快照。
    /// </summary>
    void ApplySnapshot(ClobBookEvent bookEvent);

    /// <summary>
    /// 应用增量更新。
    /// </summary>
    void ApplyDelta(ClobPriceChangeEvent priceChangeEvent);

    /// <summary>
    /// 获取订单簿完整快照。
    /// </summary>
    OrderBookSnapshot? GetSnapshot(string assetId);

    /// <summary>
    /// 获取最优买卖价。
    /// </summary>
    TopOfBook? GetTopOfBook(string assetId);

    /// <summary>
    /// 获取深度档位。
    /// </summary>
    IReadOnlyList<PriceLevel> GetDepth(string assetId, int levels = 10);

    /// <summary>
    /// 清除指定资产的订单簿。
    /// </summary>
    void Clear(string assetId);

    /// <summary>
    /// 清除所有订单簿。
    /// </summary>
    void ClearAll();
}