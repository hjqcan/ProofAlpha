using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autotrade.Polymarket.Options;
using Autotrade.Trading.Application.Contract.UserEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Trading.Application.UserEvents;

public interface IClobUserWebSocketConnection : IDisposable
{
    WebSocketState State { get; }

    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);

    Task SendTextAsync(string message, CancellationToken cancellationToken);

    Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken);

    Task CloseAsync(
        WebSocketCloseStatus closeStatus,
        string? statusDescription,
        CancellationToken cancellationToken);
}

public interface IClobUserWebSocketConnectionFactory
{
    IClobUserWebSocketConnection Create();
}

public sealed class ClientWebSocketConnectionFactory : IClobUserWebSocketConnectionFactory
{
    public IClobUserWebSocketConnection Create() => new ClientWebSocketConnection();
}

public sealed class ClientWebSocketConnection : IClobUserWebSocketConnection
{
    private readonly ClientWebSocket _webSocket = new();

    public WebSocketState State => _webSocket.State;

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken) =>
        _webSocket.ConnectAsync(uri, cancellationToken);

    public Task SendTextAsync(string message, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        return _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    public Task<WebSocketReceiveResult> ReceiveAsync(
        ArraySegment<byte> buffer,
        CancellationToken cancellationToken) =>
        _webSocket.ReceiveAsync(buffer, cancellationToken);

    public Task CloseAsync(
        WebSocketCloseStatus closeStatus,
        string? statusDescription,
        CancellationToken cancellationToken) =>
        _webSocket.CloseAsync(closeStatus, statusDescription, cancellationToken);

    public void Dispose() => _webSocket.Dispose();
}

public sealed class ClobUserOrderEventSource : IUserOrderEventSource
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly UserOrderEventSourceOptions _options;
    private readonly PolymarketClobOptions _clobOptions;
    private readonly IClobUserWebSocketConnectionFactory _webSocketFactory;
    private readonly ILogger<ClobUserOrderEventSource> _logger;
    private readonly ConcurrentDictionary<string, byte> _markets = new(StringComparer.Ordinal);
    private readonly List<Func<UserOrderEvent, CancellationToken, Task>> _orderCallbacks = [];
    private readonly List<Func<UserTradeEvent, CancellationToken, Task>> _tradeCallbacks = [];
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly object _callbackLock = new();

    private IClobUserWebSocketConnection? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Timer? _heartbeatTimer;
    private int _reconnectAttempts;
    private bool _intentionalClose;
    private bool _disposed;

    public ClobUserOrderEventSource(
        IOptions<UserOrderEventSourceOptions> options,
        IOptions<PolymarketClobOptions> clobOptions,
        ILogger<ClobUserOrderEventSource> logger)
        : this(options, clobOptions, new ClientWebSocketConnectionFactory(), logger)
    {
    }

    public ClobUserOrderEventSource(
        IOptions<UserOrderEventSourceOptions> options,
        IOptions<PolymarketClobOptions> clobOptions,
        IClobUserWebSocketConnectionFactory webSocketFactory,
        ILogger<ClobUserOrderEventSource> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _clobOptions = clobOptions?.Value ?? throw new ArgumentNullException(nameof(clobOptions));
        _webSocketFactory = webSocketFactory ?? throw new ArgumentNullException(nameof(webSocketFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsConnected => _webSocket?.State == WebSocketState.Open;

    public IReadOnlyCollection<string> SubscribedMarkets => _markets.Keys.ToList();

    public IDisposable OnOrder(Func<UserOrderEvent, CancellationToken, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (_callbackLock)
        {
            _orderCallbacks.Add(callback);
        }

        return new CallbackDisposer(() =>
        {
            lock (_callbackLock)
            {
                _orderCallbacks.Remove(callback);
            }
        });
    }

    public IDisposable OnTrade(Func<UserTradeEvent, CancellationToken, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (_callbackLock)
        {
            _tradeCallbacks.Add(callback);
        }

        return new CallbackDisposer(() =>
        {
            lock (_callbackLock)
            {
                _tradeCallbacks.Remove(callback);
            }
        });
    }

    public async Task SubscribeMarketsAsync(IEnumerable<string> marketIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(marketIds);

        var added = new List<string>();
        foreach (var marketId in marketIds)
        {
            if (string.IsNullOrWhiteSpace(marketId))
            {
                continue;
            }

            var normalized = marketId.Trim();
            if (_markets.TryAdd(normalized, 0))
            {
                added.Add(normalized);
            }
        }

        if (added.Count == 0)
        {
            return;
        }

        if (IsConnected)
        {
            await SendMarketSubscriptionOperationAsync(added, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Subscribed CLOB user channel markets: {Count}", added.Count);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsConnected)
            {
                return;
            }

            ValidateCredentials();
            _intentionalClose = false;

            await CleanupAsync().ConfigureAwait(false);

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _webSocket = _webSocketFactory.Create();

            using var timeout = new CancellationTokenSource(_options.ConnectionTimeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, timeout.Token);

            _logger.LogInformation("Connecting CLOB user WebSocket: {Url}", _options.ClobUserUrl);
            await _webSocket.ConnectAsync(new Uri(_options.ClobUserUrl), linked.Token).ConfigureAwait(false);

            _reconnectAttempts = 0;
            _receiveTask = ReceiveLoopAsync(_cts.Token);
            StartHeartbeat();
            await SendInitialUserSubscriptionAsync(_cts.Token).ConfigureAwait(false);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _intentionalClose = true;
        await CleanupAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _intentionalClose = true;
        await CleanupAsync().ConfigureAwait(false);
        _connectionLock.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task SendInitialUserSubscriptionAsync(CancellationToken cancellationToken)
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            return;
        }

        var message = new
        {
            type = "user",
            markets = _markets.Keys.ToArray(),
            auth = new
            {
                apiKey = _clobOptions.ApiKey,
                secret = _clobOptions.ApiSecret,
                passphrase = _clobOptions.ApiPassphrase
            }
        };

        await SendAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendMarketSubscriptionOperationAsync(
        IReadOnlyCollection<string> markets,
        CancellationToken cancellationToken)
    {
        if (_webSocket?.State != WebSocketState.Open || markets.Count == 0)
        {
            return;
        }

        var message = new
        {
            markets = markets.ToArray(),
            operation = "subscribe"
        };

        await SendAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendAsync<T>(T message, CancellationToken cancellationToken)
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            return;
        }

        var json = JsonSerializer.Serialize(message, JsonOptions);
        await _webSocket.SendTextAsync(json, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendRawAsync(string message, CancellationToken cancellationToken)
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            return;
        }

        await _webSocket.SendTextAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[Math.Max(4096, _options.ReceiveBufferSize)];
        using var messageBuffer = new MemoryStream();

        try
        {
            while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("CLOB user WebSocket closed: {Status} {Description}",
                        result.CloseStatus,
                        result.CloseStatusDescription);
                    break;
                }

                messageBuffer.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                {
                    continue;
                }

                var message = Encoding.UTF8.GetString(messageBuffer.ToArray());
                messageBuffer.SetLength(0);
                await ProcessMessageAsync(message, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CLOB user WebSocket receive loop failed");
        }
        finally
        {
            StopHeartbeat();

            if (!_intentionalClose && _options.AutoReconnect && !_disposed)
            {
                _ = Task.Run(ReconnectAsync, CancellationToken.None);
            }
        }
    }

    private async Task ProcessMessageAsync(string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message) ||
            message.Equals("pong", StringComparison.OrdinalIgnoreCase) ||
            message.Equals("PONG", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    await ProcessJsonObjectAsync(item, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                await ProcessJsonObjectAsync(root, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Ignored non-JSON CLOB user WebSocket message");
        }
    }

    private async Task ProcessJsonObjectAsync(JsonElement message, CancellationToken cancellationToken)
    {
        if (message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("event_type", out var eventTypeProperty))
        {
            return;
        }

        var eventType = eventTypeProperty.GetString();
        if (string.Equals(eventType, "order", StringComparison.OrdinalIgnoreCase))
        {
            var raw = message.Deserialize<RawUserOrderEvent>(JsonOptions);
            if (raw is not null && !string.IsNullOrWhiteSpace(raw.Id))
            {
                await RaiseOrderAsync(raw.ToContractEvent(), cancellationToken).ConfigureAwait(false);
            }
        }
        else if (string.Equals(eventType, "trade", StringComparison.OrdinalIgnoreCase))
        {
            var raw = message.Deserialize<RawUserTradeEvent>(JsonOptions);
            if (raw is not null && !string.IsNullOrWhiteSpace(raw.Id))
            {
                await RaiseTradeAsync(raw.ToContractEvent(), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task RaiseOrderAsync(UserOrderEvent orderEvent, CancellationToken cancellationToken)
    {
        List<Func<UserOrderEvent, CancellationToken, Task>> callbacks;
        lock (_callbackLock)
        {
            callbacks = _orderCallbacks.ToList();
        }

        foreach (var callback in callbacks)
        {
            try
            {
                await callback(orderEvent, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CLOB user order event callback failed: OrderId={OrderId}",
                    orderEvent.ExchangeOrderId);
            }
        }
    }

    private async Task RaiseTradeAsync(UserTradeEvent tradeEvent, CancellationToken cancellationToken)
    {
        List<Func<UserTradeEvent, CancellationToken, Task>> callbacks;
        lock (_callbackLock)
        {
            callbacks = _tradeCallbacks.ToList();
        }

        foreach (var callback in callbacks)
        {
            try
            {
                await callback(tradeEvent, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CLOB user trade event callback failed: TradeId={TradeId}",
                    tradeEvent.ExchangeTradeId);
            }
        }
    }

    private async Task ReconnectAsync()
    {
        if (_reconnectAttempts >= _options.MaxReconnectAttempts)
        {
            _logger.LogError("CLOB user WebSocket reached max reconnect attempts: {Attempts}", _reconnectAttempts);
            return;
        }

        _reconnectAttempts++;
        var delayMs = Math.Min(
            _options.ReconnectDelayMs * (int)Math.Pow(2, _reconnectAttempts - 1) + Random.Shared.Next(0, 1000),
            _options.MaxReconnectDelayMs);

        await Task.Delay(delayMs).ConfigureAwait(false);

        if (_intentionalClose || _disposed)
        {
            return;
        }

        try
        {
            await ConnectAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CLOB user WebSocket reconnect attempt failed: {Attempt}", _reconnectAttempts);
            _ = Task.Run(ReconnectAsync, CancellationToken.None);
        }
    }

    private void StartHeartbeat()
    {
        StopHeartbeat();
        _heartbeatTimer = new Timer(
            async _ =>
            {
                try
                {
                    await SendRawAsync("PING", CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "CLOB user WebSocket heartbeat failed");
                }
            },
            null,
            _options.ClobHeartbeatIntervalMs,
            _options.ClobHeartbeatIntervalMs);
    }

    private void StopHeartbeat()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
    }

    private async Task CleanupAsync()
    {
        StopHeartbeat();
        _cts?.Cancel();

        if (_receiveTask is not null && !_receiveTask.IsCompleted)
        {
            try
            {
                await _receiveTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger.LogDebug("Timed out waiting for CLOB user WebSocket receive loop to stop");
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_webSocket is not null)
        {
            if (_webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await _webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Client disconnect",
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Ignoring CLOB user WebSocket close failure");
                }
            }

            _webSocket.Dispose();
            _webSocket = null;
        }

        _cts?.Dispose();
        _cts = null;
        _receiveTask = null;
    }

    private void ValidateCredentials()
    {
        if (string.IsNullOrWhiteSpace(_clobOptions.ApiKey) ||
            string.IsNullOrWhiteSpace(_clobOptions.ApiSecret) ||
            string.IsNullOrWhiteSpace(_clobOptions.ApiPassphrase))
        {
            throw new InvalidOperationException(
                "Missing Polymarket CLOB API credentials for CLOB user WebSocket.");
        }
    }

    private sealed class CallbackDisposer : IDisposable
    {
        private readonly Action _dispose;
        private bool _disposed;

        public CallbackDisposer(Action dispose)
        {
            _dispose = dispose;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _dispose();
        }
    }

    private sealed record RawUserOrderEvent
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("market")]
        public string? Market { get; init; }

        [JsonPropertyName("asset_id")]
        public string? AssetId { get; init; }

        [JsonPropertyName("side")]
        public string? Side { get; init; }

        [JsonPropertyName("outcome")]
        public string? Outcome { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("original_size")]
        public string? OriginalSize { get; init; }

        [JsonPropertyName("size_matched")]
        public string? SizeMatched { get; init; }

        [JsonPropertyName("price")]
        public string? Price { get; init; }

        [JsonPropertyName("associate_trades")]
        public IReadOnlyList<string>? AssociateTrades { get; init; }

        [JsonPropertyName("timestamp")]
        public string? Timestamp { get; init; }

        [JsonPropertyName("created_at")]
        public string? CreatedAt { get; init; }

        public UserOrderEvent ToContractEvent()
        {
            return new UserOrderEvent
            {
                ExchangeOrderId = Id,
                MarketId = Market,
                TokenId = AssetId,
                Side = Side,
                Outcome = Outcome,
                Status = Status,
                Type = Type,
                OriginalSize = OriginalSize,
                SizeMatched = SizeMatched,
                Price = Price,
                AssociateTrades = AssociateTrades ?? Array.Empty<string>(),
                TimestampUtc = ParseTimestamp(Timestamp, CreatedAt)
            };
        }
    }

    private sealed record RawUserTradeEvent
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("taker_order_id")]
        public string? TakerOrderId { get; init; }

        [JsonPropertyName("market")]
        public string? Market { get; init; }

        [JsonPropertyName("asset_id")]
        public string? AssetId { get; init; }

        [JsonPropertyName("side")]
        public string? Side { get; init; }

        [JsonPropertyName("outcome")]
        public string? Outcome { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("price")]
        public string? Price { get; init; }

        [JsonPropertyName("size")]
        public string? Size { get; init; }

        [JsonPropertyName("fee_rate_bps")]
        public string? FeeRateBps { get; init; }

        [JsonPropertyName("maker_orders")]
        public IReadOnlyList<RawMakerOrder>? MakerOrders { get; init; }

        [JsonPropertyName("timestamp")]
        public string? Timestamp { get; init; }

        [JsonPropertyName("matchtime")]
        public string? MatchTime { get; init; }

        [JsonPropertyName("last_update")]
        public string? LastUpdate { get; init; }

        public UserTradeEvent ToContractEvent()
        {
            return new UserTradeEvent
            {
                ExchangeTradeId = Id,
                ExchangeOrderId = TakerOrderId,
                MarketId = Market,
                TokenId = AssetId,
                Side = Side,
                Outcome = Outcome,
                Status = Status,
                Type = Type,
                Price = Price,
                Size = Size,
                FeeRateBps = FeeRateBps,
                MakerOrders = MakerOrders?.Select(order => order.ToContractEvent()).ToArray()
                    ?? Array.Empty<UserTradeMakerOrder>(),
                TimestampUtc = ParseTimestamp(Timestamp, MatchTime, LastUpdate)
            };
        }
    }

    private sealed record RawMakerOrder
    {
        [JsonPropertyName("order_id")]
        public string? OrderId { get; init; }

        [JsonPropertyName("owner")]
        public string? Owner { get; init; }

        [JsonPropertyName("asset_id")]
        public string? AssetId { get; init; }

        [JsonPropertyName("outcome")]
        public string? Outcome { get; init; }

        [JsonPropertyName("side")]
        public string? Side { get; init; }

        [JsonPropertyName("matched_amount")]
        public string? MatchedAmount { get; init; }

        public UserTradeMakerOrder ToContractEvent()
        {
            return new UserTradeMakerOrder
            {
                ExchangeOrderId = OrderId,
                Owner = Owner,
                AssetId = AssetId,
                Outcome = Outcome,
                Side = Side,
                MatchedAmount = MatchedAmount
            };
        }
    }

    private static DateTimeOffset ParseTimestamp(params string?[] values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (DateTimeOffset.TryParse(value, out var parsed))
            {
                return parsed.ToUniversalTime();
            }

            if (long.TryParse(value, out var unix))
            {
                return unix > 10_000_000_000
                    ? DateTimeOffset.FromUnixTimeMilliseconds(unix)
                    : DateTimeOffset.FromUnixTimeSeconds(unix);
            }
        }

        return DateTimeOffset.UtcNow;
    }
}
