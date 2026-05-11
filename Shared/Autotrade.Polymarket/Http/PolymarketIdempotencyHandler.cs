using Autotrade.Polymarket;
using Microsoft.Extensions.Logging;

namespace Autotrade.Polymarket.Http;

/// <summary>
/// 为可能重复发送的写操作注入幂等键（如果调用方提供）。
/// 注意：Polymarket 是否真正支持该 header 取决于服务端实现；
/// 这里提供的是"可选支持"，不强制开启也不参与签名计算。
/// </summary>
public sealed class PolymarketIdempotencyHandler : DelegatingHandler
{
    /// <summary>
    /// 统一使用 PolymarketConstants 中定义的 header 名称。
    /// </summary>
    public const string IdempotencyKeyHeaderName = PolymarketConstants.POLY_IDEMPOTENCY_KEY_HEADER;

    public static readonly HttpRequestOptionsKey<string> IdempotencyKeyOptionKey =
        new("PolymarketIdempotencyKey");

    private readonly ILogger<PolymarketIdempotencyHandler> _logger;

    public PolymarketIdempotencyHandler(ILogger<PolymarketIdempotencyHandler> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Options.TryGetValue(IdempotencyKeyOptionKey, out var key) && !string.IsNullOrWhiteSpace(key))
        {
            request.Headers.Remove(IdempotencyKeyHeaderName);
            request.Headers.TryAddWithoutValidation(IdempotencyKeyHeaderName, key);

            _logger.LogDebug("已注入 POLY_IDEMPOTENCY_KEY（长度={Length}）：{Method} {Uri}", key.Length, request.Method, request.RequestUri);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
