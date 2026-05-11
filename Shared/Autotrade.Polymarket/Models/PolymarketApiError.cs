namespace Autotrade.Polymarket.Models;

/// <summary>
/// CLOB API 调用失败时的错误信息（用于日志与上层处理）。
/// </summary>
public sealed record PolymarketApiError(
    int StatusCode,
    string? Message,
    string? RawBody);

