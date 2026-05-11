using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Autotrade.MarketData.Application.Contract.WebSocket;
using Microsoft.Extensions.Logging;

namespace Autotrade.MarketData.Application.WebSocket.Core;

/// <summary>
/// WebSocket 客户端基类，提供连接生命周期、自动重连、心跳等功能。
/// 参考 polymarket-websocket-client/src/base-client.ts 实现。
/// </summary>
public abstract class BaseWebSocketClient : IWebSocketClient
{
    private readonly string _url;
    private readonly int _heartbeatIntervalMs;
    private readonly int _reconnectDelayMs;
    private readonly int _maxReconnectDelayMs;
    private readonly int _maxReconnectAttempts;
    private readonly int _connectionTimeoutMs;
    private readonly int _receiveBufferSize;
    private readonly bool _autoReconnect;
    protected readonly ILogger Logger;

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Timer? _heartbeatTimer;

    private ConnectionState _state = ConnectionState.Disconnected;
    private int _reconnectAttempts;
    private bool _isIntentionalClose;
    private bool _disposed;

    /// <summary>
    /// JSON 序列化选项（camelCase）。
    /// </summary>
    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    protected BaseWebSocketClient(
        string url,
        int heartbeatIntervalMs,
        int reconnectDelayMs,
        int maxReconnectDelayMs,
        int maxReconnectAttempts,
        int connectionTimeoutMs,
        int receiveBufferSize,
        bool autoReconnect,
        ILogger logger)
    {
        _url = url ?? throw new ArgumentNullException(nameof(url));
        _heartbeatIntervalMs = heartbeatIntervalMs;
        _reconnectDelayMs = reconnectDelayMs;
        _maxReconnectDelayMs = maxReconnectDelayMs;
        _maxReconnectAttempts = maxReconnectAttempts;
        _connectionTimeoutMs = connectionTimeoutMs;
        _receiveBufferSize = receiveBufferSize;
        _autoReconnect = autoReconnect;
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public ConnectionState State => _state;

    /// <inheritdoc />
    public bool IsConnected => _state == ConnectionState.Connected;

    /// <inheritdoc />
    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    public event EventHandler<WebSocketErrorEventArgs>? Error;

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_state is ConnectionState.Connected or ConnectionState.Connecting)
        {
            return;
        }

        _isIntentionalClose = false;
        await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _isIntentionalClose = true;
        await CleanupAsync().ConfigureAwait(false);
        SetState(ConnectionState.Disconnected);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _isIntentionalClose = true;
        await CleanupAsync().ConfigureAwait(false);

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 发送消息。
    /// </summary>
    protected async Task SendAsync<T>(T message, CancellationToken cancellationToken = default)
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("WebSocket 未连接");
        }

        var json = JsonSerializer.Serialize(message, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _webSocket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken).ConfigureAwait(false);

        Logger.LogDebug("已发送消息: {MessageLength} 字节", bytes.Length);
    }

    /// <summary>
    /// 发送原始字符串消息。
    /// </summary>
    protected async Task SendRawAsync(string message, CancellationToken cancellationToken = default)
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("WebSocket 未连接");
        }

        var bytes = Encoding.UTF8.GetBytes(message);
        await _webSocket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 连接成功后调用（子类重写以发送初始订阅）。
    /// </summary>
    protected virtual Task OnConnectedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// 断开连接后调用。
    /// </summary>
    protected virtual Task OnDisconnectedAsync(int code, string reason) => Task.CompletedTask;

    /// <summary>
    /// 处理解析后的消息（子类必须实现）。
    /// </summary>
    protected abstract Task HandleMessageAsync(JsonElement message, CancellationToken cancellationToken);

    /// <summary>
    /// 发送心跳消息（子类可重写）。
    /// </summary>
    protected virtual Task SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        return SendRawAsync("ping", cancellationToken);
    }

    /// <summary>
    /// 清理资源（子类可重写以释放订阅等资源）。
    /// </summary>
    protected virtual Task OnCleanupAsync() => Task.CompletedTask;

    private async Task CreateConnectionAsync(CancellationToken cancellationToken)
    {
        SetState(ConnectionState.Connecting);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _webSocket = new ClientWebSocket();

        try
        {
            using var timeoutCts = new CancellationTokenSource(_connectionTimeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, timeoutCts.Token);

            Logger.LogInformation("正在连接 WebSocket: {Url}", _url);

            await _webSocket.ConnectAsync(new Uri(_url), linkedCts.Token).ConfigureAwait(false);

            _reconnectAttempts = 0;
            SetState(ConnectionState.Connected);

            StartHeartbeat();
            _receiveTask = ReceiveLoopAsync(_cts.Token);

            await OnConnectedAsync(_cts.Token).ConfigureAwait(false);

            Logger.LogInformation("WebSocket 已连接: {Url}", _url);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Logger.LogWarning("WebSocket 连接已取消");
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "WebSocket 连接失败: {Url}", _url);
            RaiseError(ex, "连接失败");

            if (_autoReconnect && !_isIntentionalClose)
            {
                await ScheduleReconnectAsync().ConfigureAwait(false);
            }
            else
            {
                SetState(ConnectionState.Disconnected);
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[_receiveBufferSize];
        var messageBuffer = new MemoryStream();

        try
        {
            while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cancellationToken).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Logger.LogInformation("收到关闭帧: {CloseStatus} - {CloseStatusDescription}",
                        result.CloseStatus, result.CloseStatusDescription);
                    break;
                }

                messageBuffer.Write(buffer, 0, result.Count);

                if (result.EndOfMessage)
                {
                    var message = Encoding.UTF8.GetString(messageBuffer.ToArray());
                    messageBuffer.SetLength(0);

                    await ProcessMessageAsync(message, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 正常取消
        }
        catch (WebSocketException ex)
        {
            Logger.LogWarning(ex, "WebSocket 接收异常");
            RaiseError(ex, "接收消息");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "WebSocket 接收循环异常");
            RaiseError(ex, "接收循环");
        }
        finally
        {
            messageBuffer.Dispose();

            var closeCode = (int?)_webSocket?.CloseStatus ?? 0;
            var closeReason = _webSocket?.CloseStatusDescription ?? string.Empty;

            await OnDisconnectedAsync(closeCode, closeReason).ConfigureAwait(false);

            if (!_isIntentionalClose && _autoReconnect)
            {
                await ScheduleReconnectAsync().ConfigureAwait(false);
            }
            else
            {
                SetState(ConnectionState.Disconnected);
            }
        }
    }

    private async Task ProcessMessageAsync(string message, CancellationToken cancellationToken)
    {
        // 处理 pong 响应
        if (message.Equals("pong", StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogDebug("收到心跳响应: pong");
            return;
        }

        // 忽略空消息
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            // 修复：正确释放 JsonDocument
            using var jsonDoc = JsonDocument.Parse(message);
            await HandleMessageAsync(jsonDoc.RootElement, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            Logger.LogDebug(ex, "JSON 解析失败（可能是心跳或系统消息）: {Length} 字节", message.Length);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "消息处理异常");
            RaiseError(ex, "消息处理");
        }
    }

    private async Task ScheduleReconnectAsync()
    {
        if (_reconnectAttempts >= _maxReconnectAttempts)
        {
            Logger.LogError("已达最大重连次数 {MaxAttempts}，停止重连", _maxReconnectAttempts);
            SetState(ConnectionState.Disconnected);
            RaiseError(new InvalidOperationException("已达最大重连次数"), "重连");
            return;
        }

        SetState(ConnectionState.Reconnecting);
        _reconnectAttempts++;

        // 指数退避 + 抖动
        var delay = Math.Min(
            _reconnectDelayMs * (int)Math.Pow(2, _reconnectAttempts - 1) + Random.Shared.Next(0, 1000),
            _maxReconnectDelayMs);

        Logger.LogInformation("将在 {DelayMs}ms 后重连 (尝试 {Attempt}/{MaxAttempts})",
            delay, _reconnectAttempts, _maxReconnectAttempts);

        await Task.Delay(delay).ConfigureAwait(false);

        if (!_isIntentionalClose)
        {
            await CleanupAsync(preserveState: true).ConfigureAwait(false);
            await CreateConnectionAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private void StartHeartbeat()
    {
        StopHeartbeat();

        _heartbeatTimer = new Timer(
            async _ =>
            {
                if (_webSocket?.State == WebSocketState.Open)
                {
                    try
                    {
                        await SendHeartbeatAsync(CancellationToken.None).ConfigureAwait(false);
                        Logger.LogDebug("已发送心跳");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "心跳发送失败");
                    }
                }
            },
            null,
            _heartbeatIntervalMs,
            _heartbeatIntervalMs);
    }

    private void StopHeartbeat()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
    }

    private async Task CleanupAsync(bool preserveState = false)
    {
        StopHeartbeat();

        _cts?.Cancel();

        if (_receiveTask != null)
        {
            try
            {
                await _receiveTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                Logger.LogWarning("等待接收任务超时");
            }
            catch (OperationCanceledException)
            {
                // 预期的取消
            }
        }

        if (_webSocket != null)
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
                    Logger.LogDebug(ex, "关闭 WebSocket 时异常（可忽略）");
                }
            }

            _webSocket.Dispose();
            _webSocket = null;
        }

        _cts?.Dispose();
        _cts = null;
        _receiveTask = null;

        await OnCleanupAsync().ConfigureAwait(false);

        if (!preserveState)
        {
            _reconnectAttempts = 0;
        }
    }

    private void SetState(ConnectionState newState)
    {
        if (_state == newState) return;

        var previousState = _state;
        _state = newState;

        Logger.LogDebug("连接状态变更: {PreviousState} -> {CurrentState}", previousState, newState);
        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(previousState, newState));
    }

    private void RaiseError(Exception exception, string? context = null)
    {
        Error?.Invoke(this, new WebSocketErrorEventArgs(exception, context));
    }
}
