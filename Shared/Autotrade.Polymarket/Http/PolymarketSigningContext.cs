namespace Autotrade.Polymarket.Http;

/// <summary>
/// 用于在 HttpClient 管道中进行签名的上下文信息。
/// </summary>
public sealed record PolymarketSigningContext(
    PolymarketAuthLevel AuthLevel,
    string RequestPath,
    string? SerializedBody,
    int? Nonce = null,
    long? TimestampSeconds = null);

