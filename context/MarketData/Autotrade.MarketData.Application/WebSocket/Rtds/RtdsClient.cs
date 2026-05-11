using System.Collections.Concurrent;
using System.Text.Json;
using Autotrade.MarketData.Application.Contract.WebSocket.Events;
using Autotrade.MarketData.Application.Observability;
using Autotrade.MarketData.Application.WebSocket.Core;
using Autotrade.MarketData.Application.WebSocket.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.MarketData.Application.WebSocket.Rtds;

/// <summary>
/// RTDS（Real-Time Data Socket）客户端，用于订阅加密货币实时价格等数据。
/// 这是实现"重定价延迟套利"策略的关键组件！
/// 参考 polymarket-websocket-client/src/rtds-client.ts 实现。
/// </summary>
public sealed class RtdsClient : BaseWebSocketClient, IRtdsClient
{
    private readonly ConcurrentDictionary<string, RtdsSubscription> _subscriptions = new();
    private readonly List<Func<CryptoPriceEvent, Task>> _cryptoPriceCallbacks = new();
    private readonly object _callbackLock = new();
    private readonly object _cryptoPricesFilterLock = new();
    /// <summary>
    /// 客户端符号过滤器（小写）。用于多符号订阅时的客户端过滤。
    /// 因为 RTDS 每个订阅只能过滤一个符号，其余符号由客户端过滤。
    /// </summary>
    private HashSet<string>? _cryptoPricesClientFilter;

    public RtdsClient(IOptions<PolymarketWebSocketOptions> options, ILogger<RtdsClient> logger)
        : base(
            options.Value.RtdsUrl,
            options.Value.RtdsHeartbeatIntervalMs,
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
    public async Task SubscribeCryptoPricesAsync(IEnumerable<string>? symbols = null, CancellationToken cancellationToken = default)
    {
        // 官方格式（来自 https://github.com/Polymarket/real-time-data-client README）：
        // filters 是 JSON 对象格式 {"symbol":"BTCUSDT"}，符号大写
        // 
        // 重要发现（经实际测试验证 2026-01-04）：
        // - RTDS 每个 topic 只能有一个活跃 filter
        // - 发送多个同 topic 订阅时，只有第一个或最后一个生效（取决于发送方式）
        // - 省略 filters 字段 等效于 filters="" —— 两者都接收所有数据（已测试验证）
        //
        // 当前策略：
        // - 单符号：使用 {"symbol":"XXX"} 服务端过滤（减少带宽）
        // - 多符号或全部：省略 filters 字段（接收所有数据），客户端过滤
        var normalizedSymbols = symbols?
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // 设置客户端过滤器（小写匹配，因为服务端推送的 symbol 是小写）
        HashSet<string>? clientFilter = null;
        if (normalizedSymbols is { Length: > 0 })
        {
            clientFilter = new HashSet<string>(
                normalizedSymbols.Select(s => s.ToLowerInvariant()),
                StringComparer.OrdinalIgnoreCase);
        }

        lock (_cryptoPricesFilterLock)
        {
            _cryptoPricesClientFilter = clientFilter;
        }

        // 确定 filters 参数
        string filters;
        if (normalizedSymbols is { Length: 1 })
        {
            // 单符号：使用服务端过滤
            filters = JsonSerializer.Serialize(new { symbol = normalizedSymbols[0] });
        }
        else
        {
            // 多符号或全部：使用空过滤器接收所有数据，客户端过滤
            filters = "";
        }

        var subscription = new RtdsSubscription("crypto_prices", "update", filters);

        var key = GetSubscriptionKey(subscription);
        _subscriptions[key] = subscription;

        if (IsConnected)
        {
            await SendSubscriptionAsync(new[] { subscription }, "subscribe", cancellationToken).ConfigureAwait(false);
        }

        var logSymbols = normalizedSymbols is { Length: > 0 } ? string.Join(",", normalizedSymbols) : "(all)";
        var filterMode = normalizedSymbols is { Length: 1 } ? "server" : "client";
        Logger.LogInformation("已订阅 crypto_prices (Binance): {Symbols} (filters={Filters}, mode={Mode})",
            logSymbols, filters.Length > 0 ? filters : "(empty)", filterMode);
    }

    /// <inheritdoc />
    public async Task UnsubscribeCryptoPricesAsync(CancellationToken cancellationToken = default)
    {
        var key = "crypto_prices:update";
        if (_subscriptions.TryRemove(key, out var subscription))
        {
            if (IsConnected)
            {
                await SendSubscriptionAsync(new[] { subscription }, "unsubscribe", cancellationToken).ConfigureAwait(false);
            }
        }

        lock (_cryptoPricesFilterLock)
        {
            _cryptoPricesClientFilter = null;
        }
    }

    /// <inheritdoc />
    public async Task SubscribeCryptoPricesChainlinkAsync(string? symbol = null, CancellationToken cancellationToken = default)
    {
        var filters = symbol != null ? JsonSerializer.Serialize(new { symbol }) : "";
        var subscription = new RtdsSubscription("crypto_prices_chainlink", "*", filters);

        var key = GetSubscriptionKey(subscription);
        _subscriptions[key] = subscription;

        if (IsConnected)
        {
            await SendSubscriptionAsync(new[] { subscription }, "subscribe", cancellationToken).ConfigureAwait(false);
        }

        Logger.LogInformation("已订阅 crypto_prices_chainlink: {Symbol}", symbol ?? "all");
    }

    /// <inheritdoc />
    public async Task UnsubscribeCryptoPricesChainlinkAsync(CancellationToken cancellationToken = default)
    {
        var key = "crypto_prices_chainlink:*";
        if (_subscriptions.TryRemove(key, out var subscription))
        {
            if (IsConnected)
            {
                await SendSubscriptionAsync(new[] { subscription }, "unsubscribe", cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public IDisposable OnCryptoPrice(Func<CryptoPriceEvent, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (_callbackLock)
        {
            _cryptoPriceCallbacks.Add(callback);
        }

        return new CallbackDisposer(() =>
        {
            lock (_callbackLock)
            {
                _cryptoPriceCallbacks.Remove(callback);
            }
        });
    }

    protected override async Task OnConnectedAsync(CancellationToken cancellationToken)
    {
        // 重连后重新订阅
        var subscriptions = _subscriptions.Values.ToList();
        if (subscriptions.Count > 0)
        {
            await SendSubscriptionAsync(subscriptions, "subscribe", cancellationToken).ConfigureAwait(false);
            Logger.LogInformation("重连后重新订阅 {Count} 个主题", subscriptions.Count);
        }
    }

    protected override Task OnCleanupAsync()
    {
        // 修复：不清空业务回调！回调由消费者注册，
        // 重连后需要保留，否则断线重连后没有任何回调触发，行情静默丢失。
        return Task.CompletedTask;
    }

    protected override async Task SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        // RTDS 需要发送 JSON 格式的 PING
        await SendAsync(new { type = "PING" }, cancellationToken).ConfigureAwait(false);
    }

    protected override async Task HandleMessageAsync(JsonElement message, CancellationToken cancellationToken)
    {
        if (!message.TryGetProperty("topic", out var topicProp))
        {
            await HandleSystemMessageAsync(message, cancellationToken).ConfigureAwait(false);
            return;
        }

        var topic = topicProp.GetString();

        switch (topic)
        {
            case "crypto_prices":
                await HandleCryptoPriceEventAsync(message, applyCryptoPricesClientFilter: true, cancellationToken).ConfigureAwait(false);
                break;
            case "crypto_prices_chainlink":
                await HandleCryptoPriceEventAsync(message, applyCryptoPricesClientFilter: false, cancellationToken).ConfigureAwait(false);
                break;
            default:
                Logger.LogDebug("未处理的 RTDS 主题: {Topic}", topic);
                break;
        }
    }

    private Task HandleSystemMessageAsync(JsonElement message, CancellationToken cancellationToken)
    {
        // RTDS 会发送非 topic 的系统消息（包含订阅确认、错误等）
        int? statusCode = null;
        if (message.TryGetProperty("statusCode", out var statusCodeProp) && statusCodeProp.TryGetInt32(out var code))
        {
            statusCode = code;
        }

        string? serverMessage = null;
        if (message.TryGetProperty("body", out var bodyProp)
            && bodyProp.ValueKind == JsonValueKind.Object
            && bodyProp.TryGetProperty("message", out var bodyMessageProp)
            && bodyMessageProp.ValueKind == JsonValueKind.String)
        {
            serverMessage = bodyMessageProp.GetString();
        }
        else if (message.TryGetProperty("message", out var messageProp) && messageProp.ValueKind == JsonValueKind.String)
        {
            serverMessage = messageProp.GetString();
        }

        if (statusCode is >= 400)
        {
            Logger.LogWarning("RTDS 系统错误: statusCode={StatusCode} message={Message}", statusCode, serverMessage);
        }
        else if (!string.IsNullOrWhiteSpace(serverMessage) || statusCode.HasValue)
        {
            Logger.LogDebug("RTDS 系统消息: statusCode={StatusCode} message={Message}", statusCode, serverMessage);
        }

        return Task.CompletedTask;
    }

    private async Task HandleCryptoPriceEventAsync(
        JsonElement message,
        bool applyCryptoPricesClientFilter,
        CancellationToken cancellationToken)
    {
        var cryptoPriceEvent = JsonSerializer.Deserialize<CryptoPriceEvent>(message.GetRawText(), JsonOptions);
        if (cryptoPriceEvent == null) return;

        // Subscription lag：事件时间戳到本地接收时间的延迟
        // topic 低基数（crypto_prices / crypto_prices_chainlink）
        var eventTime = DateTimeOffset.FromUnixTimeMilliseconds(cryptoPriceEvent.Payload.Timestamp);
        var lagMs = (DateTimeOffset.UtcNow - eventTime).TotalMilliseconds;
        if (lagMs >= 0)
        {
            MarketDataMetrics.MessageLagMs.Record(lagMs,
                new KeyValuePair<string, object?>("topic", cryptoPriceEvent.Topic));
        }

        var symbol = cryptoPriceEvent.Payload.Symbol;
        if (applyCryptoPricesClientFilter)
        {
            HashSet<string>? filter;
            lock (_cryptoPricesFilterLock)
            {
                filter = _cryptoPricesClientFilter;
            }

            if (filter != null && !filter.Contains(symbol))
            {
                return;
            }
        }

        Logger.LogDebug("收到加密货币价格: {Symbol} = {Price}",
            cryptoPriceEvent.Payload.Symbol, cryptoPriceEvent.Payload.Value);

        List<Func<CryptoPriceEvent, Task>> callbacks;
        lock (_callbackLock)
        {
            callbacks = _cryptoPriceCallbacks.ToList();
        }

        foreach (var callback in callbacks)
        {
            try
            {
                await callback(cryptoPriceEvent).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "CryptoPrice 回调执行异常");
            }
        }
    }

    private async Task SendSubscriptionAsync(IEnumerable<RtdsSubscription> subscriptions, string action, CancellationToken cancellationToken)
    {
        // 按照官方文档格式构建订阅消息
        // filters 是普通字符串（逗号分隔的符号或 JSON 字符串）
        var subscriptionList = subscriptions.Select(s =>
        {
            var dict = new Dictionary<string, object>
            {
                ["topic"] = s.Topic,
                ["type"] = s.Type
            };

            // 只有当 filters 非空时才添加到消息中
            if (!string.IsNullOrEmpty(s.Filters))
            {
                dict["filters"] = s.Filters;
            }

            return dict;
        }).ToList();

        var message = new
        {
            action,
            subscriptions = subscriptionList
        };

        await SendAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private static string GetSubscriptionKey(RtdsSubscription subscription)
    {
        return $"{subscription.Topic}:{subscription.Type}";
    }

    private sealed record RtdsSubscription(string Topic, string Type, string? Filters);

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
