using Autotrade.MarketData.Application.Contract.WebSocket.Events;
using Autotrade.MarketData.Application.WebSocket.Core;

namespace Autotrade.MarketData.Application.WebSocket.Rtds;

/// <summary>
/// RTDS 客户端接口，用于订阅实时数据流。
/// </summary>
public interface IRtdsClient : IWebSocketClient
{
    /// <summary>
    /// 订阅加密货币价格（来自 Binance）。
    /// </summary>
    /// <param name="symbols">可选的币种列表（如 "btcusdt", "ethusdt"）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SubscribeCryptoPricesAsync(IEnumerable<string>? symbols = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取消订阅加密货币价格（Binance）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task UnsubscribeCryptoPricesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 订阅加密货币价格（来自 Chainlink）。
    /// </summary>
    /// <param name="symbol">可选的币种（如 "btc/usd"）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SubscribeCryptoPricesChainlinkAsync(string? symbol = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取消订阅加密货币价格（Chainlink）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task UnsubscribeCryptoPricesChainlinkAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 注册加密货币价格事件回调。
    /// </summary>
    /// <param name="callback">回调函数。</param>
    /// <returns>用于取消注册的 Disposable。</returns>
    IDisposable OnCryptoPrice(Func<CryptoPriceEvent, Task> callback);
}
