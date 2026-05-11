namespace Autotrade.MarketData.Application.WebSocket.Options;

/// <summary>
/// Polymarket WebSocket 客户端配置选项。
/// </summary>
public sealed class PolymarketWebSocketOptions
{
    /// <summary>
    /// 配置节点名称。
    /// </summary>
    public const string SectionName = "Polymarket:WebSocket";

    /// <summary>
    /// CLOB Market 通道 URL（公开，无需鉴权）。
    /// </summary>
    public string ClobMarketUrl { get; set; } = "wss://ws-subscriptions-clob.polymarket.com/ws/market";

    /// <summary>
    /// CLOB User 通道 URL（需要 API Key 鉴权）。
    /// </summary>
    public string ClobUserUrl { get; set; } = "wss://ws-subscriptions-clob.polymarket.com/ws/user";

    /// <summary>
    /// RTDS 实时数据流 URL（用于 crypto_prices 等）。
    /// </summary>
    public string RtdsUrl { get; set; } = "wss://ws-live-data.polymarket.com";

    /// <summary>
    /// 是否启用自动重连（默认 true）。
    /// </summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    /// 最大重连尝试次数（默认无限）。
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = int.MaxValue;

    /// <summary>
    /// 重连基础延迟（毫秒，默认 1000ms）。
    /// </summary>
    public int ReconnectDelayMs { get; set; } = 1000;

    /// <summary>
    /// 最大重连延迟（毫秒，默认 30000ms）。
    /// </summary>
    public int MaxReconnectDelayMs { get; set; } = 30000;

    /// <summary>
    /// CLOB 心跳间隔（毫秒，默认 30000ms）。
    /// </summary>
    public int ClobHeartbeatIntervalMs { get; set; } = 30000;

    /// <summary>
    /// RTDS 心跳间隔（毫秒，默认 5000ms）。
    /// </summary>
    public int RtdsHeartbeatIntervalMs { get; set; } = 5000;

    /// <summary>
    /// 连接超时（毫秒，默认 10000ms）。
    /// </summary>
    public int ConnectionTimeoutMs { get; set; } = 10000;

    /// <summary>
    /// 接收缓冲区大小（字节，默认 64KB）。
    /// </summary>
    public int ReceiveBufferSize { get; set; } = 64 * 1024;
}
