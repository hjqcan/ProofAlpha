namespace Autotrade.Polymarket.Models;

/// <summary>
/// CLOB API 结果包装：避免在上层到处 try/catch。
/// </summary>
public sealed record PolymarketApiResult<T>(
    bool IsSuccess,
    int StatusCode,
    T? Data,
    PolymarketApiError? Error)
{
    public static PolymarketApiResult<T> Success(int statusCode, T data) =>
        new(true, statusCode, data, null);

    public static PolymarketApiResult<T> Failure(int statusCode, string? message, string? rawBody) =>
        new(false, statusCode, default, new PolymarketApiError(statusCode, message, rawBody));
}

