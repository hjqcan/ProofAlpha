using Autotrade.MarketData.Application.Contract.WebSocket;

namespace Autotrade.MarketData.Application.WebSocket.Core;

/// <summary>
/// WebSocket 客户端接口。
/// </summary>
public interface IWebSocketClient : IAsyncDisposable
{
    /// <summary>
    /// 当前连接状态。
    /// </summary>
    ConnectionState State { get; }

    /// <summary>
    /// 是否已连接。
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 连接状态变更事件。
    /// </summary>
    event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// 错误事件。
    /// </summary>
    event EventHandler<WebSocketErrorEventArgs>? Error;

    /// <summary>
    /// 连接到 WebSocket 服务器。
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 断开连接。
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 连接状态变更事件参数。
/// </summary>
public sealed class ConnectionStateChangedEventArgs : EventArgs
{
    public ConnectionState PreviousState { get; }
    public ConnectionState CurrentState { get; }

    public ConnectionStateChangedEventArgs(ConnectionState previousState, ConnectionState currentState)
    {
        PreviousState = previousState;
        CurrentState = currentState;
    }
}

/// <summary>
/// WebSocket 错误事件参数。
/// </summary>
public sealed class WebSocketErrorEventArgs : EventArgs
{
    public Exception Exception { get; }
    public string? Context { get; }

    public WebSocketErrorEventArgs(Exception exception, string? context = null)
    {
        Exception = exception;
        Context = context;
    }
}
