using Autotrade.MarketData.Application.Contract.WebSocket.Events;
using Autotrade.MarketData.Application.WebSocket.Core;

namespace Autotrade.MarketData.Application.WebSocket.Clob;

/// <summary>
/// CLOB Market 通道客户端接口。
/// </summary>
public interface IClobMarketClient : IWebSocketClient
{
    /// <summary>
    /// 当前已订阅的资产 ID 列表。
    /// </summary>
    IReadOnlyCollection<string> SubscribedAssets { get; }

    /// <summary>
    /// 订阅资产的订单簿更新。
    /// </summary>
    /// <param name="assetIds">资产 ID（Token ID）列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SubscribeAsync(IEnumerable<string> assetIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取消订阅资产的订单簿更新。
    /// </summary>
    /// <param name="assetIds">资产 ID 列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task UnsubscribeAsync(IEnumerable<string> assetIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// 注册订单簿快照事件回调。
    /// </summary>
    /// <param name="assetId">资产 ID。</param>
    /// <param name="callback">回调函数。</param>
    /// <returns>用于取消注册的 Disposable。</returns>
    IDisposable OnBook(string assetId, Func<ClobBookEvent, Task> callback);

    /// <summary>
    /// 注册价格变动事件回调。
    /// </summary>
    /// <param name="assetId">资产 ID。</param>
    /// <param name="callback">回调函数。</param>
    /// <returns>用于取消注册的 Disposable。</returns>
    IDisposable OnPriceChange(string assetId, Func<ClobPriceChangeEvent, Task> callback);

    /// <summary>
    /// 注册最后成交价事件回调。
    /// </summary>
    /// <param name="callback">回调函数。</param>
    /// <returns>用于取消注册的 Disposable。</returns>
    IDisposable OnLastTradePrice(Func<ClobLastTradePriceEvent, Task> callback);

    /// <summary>
    /// 注册 Tick Size 变更事件回调。
    /// </summary>
    /// <param name="callback">回调函数。</param>
    /// <returns>用于取消注册的 Disposable。</returns>
    IDisposable OnTickSizeChange(Func<ClobTickSizeChangeEvent, Task> callback);
}
