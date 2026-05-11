using System.Collections.Concurrent;
using System.Text.Json;
using Autotrade.MarketData.Application.Contract.WebSocket.Events;
using Autotrade.MarketData.Application.WebSocket.Core;
using Autotrade.MarketData.Application.WebSocket.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.MarketData.Application.WebSocket.Clob;

/// <summary>
/// CLOB Market 通道客户端（公开通道，用于订阅订单簿更新）。
/// 参考 polymarket-websocket-client/src/clob-client.ts 实现。
/// </summary>
public sealed class ClobMarketClient : BaseWebSocketClient, IClobMarketClient
{
    private readonly ConcurrentDictionary<string, byte> _subscribedAssets = new();
    private readonly ConcurrentDictionary<string, List<Func<ClobBookEvent, Task>>> _bookCallbacks = new();
    private readonly ConcurrentDictionary<string, List<Func<ClobPriceChangeEvent, Task>>> _priceChangeCallbacks = new();
    private readonly List<Func<ClobLastTradePriceEvent, Task>> _lastTradePriceCallbacks = new();
    private readonly List<Func<ClobTickSizeChangeEvent, Task>> _tickSizeChangeCallbacks = new();
    private readonly object _callbackLock = new();

    public ClobMarketClient(IOptions<PolymarketWebSocketOptions> options, ILogger<ClobMarketClient> logger)
        : base(
            options.Value.ClobMarketUrl,
            options.Value.ClobHeartbeatIntervalMs,
            options.Value.ReconnectDelayMs,
            options.Value.MaxReconnectDelayMs,
            options.Value.MaxReconnectAttempts,
            options.Value.ConnectionTimeoutMs,
            options.Value.ReceiveBufferSize,
            options.Value.AutoReconnect,
            logger)
    {
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> SubscribedAssets => _subscribedAssets.Keys.ToList();

    /// <inheritdoc />
    public async Task SubscribeAsync(IEnumerable<string> assetIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);

        var newAssets = new List<string>();
        foreach (var id in assetIds)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Asset ID 不能为空", nameof(assetIds));
            }

            if (_subscribedAssets.TryAdd(id, 0))
            {
                newAssets.Add(id);
            }
        }

        if (newAssets.Count == 0) return;

        if (IsConnected)
        {
            await SendSubscriptionAsync(newAssets, "subscribe", cancellationToken).ConfigureAwait(false);
        }

        Logger.LogInformation("已订阅 {Count} 个资产: {AssetIds}", newAssets.Count, string.Join(", ", newAssets.Take(5)));
    }

    /// <inheritdoc />
    public async Task UnsubscribeAsync(IEnumerable<string> assetIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);

        var removedAssets = new List<string>();
        foreach (var id in assetIds)
        {
            if (_subscribedAssets.TryRemove(id, out _))
            {
                removedAssets.Add(id);
            }
        }

        if (removedAssets.Count == 0) return;

        // 尝试发送取消订阅消息，但不因连接已断开而失败
        if (IsConnected)
        {
            try
            {
                await SendSubscriptionAsync(removedAssets, "unsubscribe", cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // WebSocket 可能在检查 IsConnected 后断开，忽略此错误
                Logger.LogDebug("取消订阅时 WebSocket 已断开，跳过发送");
            }
        }

        Logger.LogInformation("已取消订阅 {Count} 个资产", removedAssets.Count);
    }

    /// <inheritdoc />
    public IDisposable OnBook(string assetId, Func<ClobBookEvent, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (_callbackLock)
        {
            if (!_bookCallbacks.TryGetValue(assetId, out var list))
            {
                list = new List<Func<ClobBookEvent, Task>>();
                _bookCallbacks[assetId] = list;
            }
            list.Add(callback);
        }

        return new CallbackDisposer(() =>
        {
            lock (_callbackLock)
            {
                if (_bookCallbacks.TryGetValue(assetId, out var list))
                {
                    list.Remove(callback);
                }
            }
        });
    }

    /// <inheritdoc />
    public IDisposable OnPriceChange(string assetId, Func<ClobPriceChangeEvent, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (_callbackLock)
        {
            if (!_priceChangeCallbacks.TryGetValue(assetId, out var list))
            {
                list = new List<Func<ClobPriceChangeEvent, Task>>();
                _priceChangeCallbacks[assetId] = list;
            }
            list.Add(callback);
        }

        return new CallbackDisposer(() =>
        {
            lock (_callbackLock)
            {
                if (_priceChangeCallbacks.TryGetValue(assetId, out var list))
                {
                    list.Remove(callback);
                }
            }
        });
    }

    /// <inheritdoc />
    public IDisposable OnLastTradePrice(Func<ClobLastTradePriceEvent, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (_callbackLock)
        {
            _lastTradePriceCallbacks.Add(callback);
        }

        return new CallbackDisposer(() =>
        {
            lock (_callbackLock)
            {
                _lastTradePriceCallbacks.Remove(callback);
            }
        });
    }

    /// <inheritdoc />
    public IDisposable OnTickSizeChange(Func<ClobTickSizeChangeEvent, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (_callbackLock)
        {
            _tickSizeChangeCallbacks.Add(callback);
        }

        return new CallbackDisposer(() =>
        {
            lock (_callbackLock)
            {
                _tickSizeChangeCallbacks.Remove(callback);
            }
        });
    }

    protected override async Task OnConnectedAsync(CancellationToken cancellationToken)
    {
        // 重连后重新订阅所有资产
        var assets = _subscribedAssets.Keys.ToList();
        if (assets.Count > 0)
        {
            var message = new
            {
                type = "MARKET",
                assets_ids = assets
            };
            await SendAsync(message, cancellationToken).ConfigureAwait(false);
            Logger.LogInformation("重连后重新订阅 {Count} 个资产", assets.Count);
        }
    }

    protected override Task OnCleanupAsync()
    {
        // 修复：不清空业务回调！回调由 OrderBookSynchronizer 等消费者注册，
        // 重连后需要保留，否则断线重连后没有任何回调触发，行情静默丢失。
        // 只清理连接级资源（当前没有额外的连接级资源需要清理）。
        return Task.CompletedTask;
    }

    protected override async Task HandleMessageAsync(JsonElement message, CancellationToken cancellationToken)
    {
        // CLOB WebSocket 可能返回数组（多个事件）或单个对象
        if (message.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in message.EnumerateArray())
            {
                await HandleSingleMessageAsync(item, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            await HandleSingleMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleSingleMessageAsync(JsonElement message, CancellationToken cancellationToken)
    {
        if (!message.TryGetProperty("event_type", out var eventTypeProp))
        {
            return;
        }

        var eventType = eventTypeProp.GetString();

        switch (eventType)
        {
            case "book":
                await HandleBookEventAsync(message, cancellationToken).ConfigureAwait(false);
                break;
            case "price_change":
                await HandlePriceChangeEventAsync(message, cancellationToken).ConfigureAwait(false);
                break;
            case "last_trade_price":
                await HandleLastTradePriceEventAsync(message, cancellationToken).ConfigureAwait(false);
                break;
            case "tick_size_change":
                await HandleTickSizeChangeEventAsync(message, cancellationToken).ConfigureAwait(false);
                break;
            default:
                Logger.LogDebug("未知事件类型: {EventType}", eventType);
                break;
        }
    }

    private async Task HandleBookEventAsync(JsonElement message, CancellationToken cancellationToken)
    {
        var bookEvent = JsonSerializer.Deserialize<ClobBookEvent>(message.GetRawText(), JsonOptions);
        if (bookEvent == null) return;

        Logger.LogDebug("收到订单簿快照: {AssetId}, Bids={BidCount}, Asks={AskCount}",
            bookEvent.AssetId, bookEvent.Bids.Count, bookEvent.Asks.Count);

        List<Func<ClobBookEvent, Task>> callbacks;
        lock (_callbackLock)
        {
            if (!_bookCallbacks.TryGetValue(bookEvent.AssetId, out var list))
            {
                return;
            }
            callbacks = list.ToList();
        }

        foreach (var callback in callbacks)
        {
            try
            {
                await callback(bookEvent).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Book 回调执行异常");
            }
        }
    }

    private async Task HandlePriceChangeEventAsync(JsonElement message, CancellationToken cancellationToken)
    {
        var priceChangeEvent = JsonSerializer.Deserialize<ClobPriceChangeEvent>(message.GetRawText(), JsonOptions);
        if (priceChangeEvent == null) return;

        // 修复：按 assetId 分组触发回调，每次只传递该资产的单条变更
        // 这样 OrderBookSynchronizer 只处理属于自己 assetId 的 change，避免重复应用
        foreach (var change in priceChangeEvent.PriceChanges)
        {
            List<Func<ClobPriceChangeEvent, Task>> callbacks;
            lock (_callbackLock)
            {
                if (!_priceChangeCallbacks.TryGetValue(change.AssetId, out var list))
                {
                    continue;
                }
                callbacks = list.ToList();
            }

            // 创建仅包含该 assetId 的单条 change 的事件
            var singleChangeEvent = priceChangeEvent with
            {
                PriceChanges = new[] { change }
            };

            foreach (var callback in callbacks)
            {
                try
                {
                    await callback(singleChangeEvent).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "PriceChange 回调执行异常: {AssetId}", change.AssetId);
                }
            }
        }
    }

    private async Task HandleLastTradePriceEventAsync(JsonElement message, CancellationToken cancellationToken)
    {
        var lastTradePriceEvent = JsonSerializer.Deserialize<ClobLastTradePriceEvent>(message.GetRawText(), JsonOptions);
        if (lastTradePriceEvent == null) return;

        List<Func<ClobLastTradePriceEvent, Task>> callbacks;
        lock (_callbackLock)
        {
            callbacks = _lastTradePriceCallbacks.ToList();
        }

        foreach (var callback in callbacks)
        {
            try
            {
                await callback(lastTradePriceEvent).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "LastTradePrice 回调执行异常");
            }
        }
    }

    private async Task HandleTickSizeChangeEventAsync(JsonElement message, CancellationToken cancellationToken)
    {
        var tickSizeChangeEvent = JsonSerializer.Deserialize<ClobTickSizeChangeEvent>(message.GetRawText(), JsonOptions);
        if (tickSizeChangeEvent == null) return;

        List<Func<ClobTickSizeChangeEvent, Task>> callbacks;
        lock (_callbackLock)
        {
            callbacks = _tickSizeChangeCallbacks.ToList();
        }

        foreach (var callback in callbacks)
        {
            try
            {
                await callback(tickSizeChangeEvent).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "TickSizeChange 回调执行异常");
            }
        }
    }

    private async Task SendSubscriptionAsync(List<string> assetIds, string operation, CancellationToken cancellationToken)
    {
        var message = new
        {
            assets_ids = assetIds,
            operation
        };
        await SendAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private sealed class CallbackDisposer : IDisposable
    {
        private readonly Action _disposeAction;
        private bool _disposed;

        public CallbackDisposer(Action disposeAction)
        {
            _disposeAction = disposeAction;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _disposeAction();
        }
    }
}
