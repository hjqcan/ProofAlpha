namespace Autotrade.MarketData.Application.Contract.WebSocket.Events;

/// <summary>
/// RTDS 加密货币实时价格事件（来自 Binance 或 Chainlink）。
/// 这是实现"重定价延迟套利"策略的关键数据源！
/// </summary>
public sealed record CryptoPriceEvent
{
    public required string Topic { get; init; } // "crypto_prices" or "crypto_prices_chainlink"
    public required string Type { get; init; }  // "update"
    public required long Timestamp { get; init; }
    public required CryptoPricePayload Payload { get; init; }
}

/// <summary>
/// 加密货币价格负载。
/// </summary>
public sealed record CryptoPricePayload
{
    public required string Symbol { get; init; }   // e.g., "btcusdt"
    public required long Timestamp { get; init; }
    public required decimal Value { get; init; }   // e.g., 67234.5
}
