namespace Autotrade.Trading.Application.Contract.UserEvents;

public sealed class UserOrderEventSourceOptions
{
    public const string SectionName = "Polymarket:WebSocket";

    public string ClobUserUrl { get; set; } = "wss://ws-subscriptions-clob.polymarket.com/ws/user";

    public bool AutoReconnect { get; set; } = true;

    public int ReconnectDelayMs { get; set; } = 1000;

    public int MaxReconnectDelayMs { get; set; } = 30000;

    public int MaxReconnectAttempts { get; set; } = int.MaxValue;

    public int ClobHeartbeatIntervalMs { get; set; } = 10000;

    public int ConnectionTimeoutMs { get; set; } = 10000;

    public int ReceiveBufferSize { get; set; } = 64 * 1024;
}
