namespace Autotrade.MarketData.Application.Contract.WebSocket;

/// <summary>
/// WebSocket 连接状态。
/// </summary>
public enum ConnectionState
{
    /// <summary>已断开连接。</summary>
    Disconnected,

    /// <summary>正在连接。</summary>
    Connecting,

    /// <summary>已连接。</summary>
    Connected,

    /// <summary>正在重连。</summary>
    Reconnecting
}
