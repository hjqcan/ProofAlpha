using System.Threading.RateLimiting;
using Autotrade.Polymarket.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Polymarket.Http;

/// <summary>
/// 客户端侧限流：并发门 + 令牌桶（全局）。
/// 后续如需“按端点限流”，可在此基础上按 requestPath 细分 limiter。
/// </summary>
public sealed class PolymarketRateLimitHandler : DelegatingHandler
{
    private readonly ILogger<PolymarketRateLimitHandler> _logger;
    private readonly ConcurrencyLimiter? _concurrencyLimiter;
    private readonly TokenBucketRateLimiter? _tokenBucket;
    private readonly bool _enabled;

    public PolymarketRateLimitHandler(
        IOptions<PolymarketRateLimitOptions> options,
        ILogger<PolymarketRateLimitHandler> logger)
    {
        var opt = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _enabled = opt.Enabled;

        if (!_enabled)
        {
            return;
        }

        var queueLimit = Math.Max(0, opt.MaxConcurrency * 20);

        _concurrencyLimiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = Math.Max(1, opt.MaxConcurrency),
            QueueLimit = queueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });

        _tokenBucket = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = Math.Max(1, opt.TokenBucketCapacity),
            TokensPerPeriod = Math.Max(1, opt.TokensPerPeriod),
            ReplenishmentPeriod = opt.ReplenishmentPeriod <= TimeSpan.Zero
                ? TimeSpan.FromSeconds(10)
                : opt.ReplenishmentPeriod,
            QueueLimit = queueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // 如果限流被禁用，直接透传
        if (!_enabled)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        // 1) 并发门
        using var concurrencyLease = await _concurrencyLimiter!.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
        if (!concurrencyLease.IsAcquired)
        {
            _logger.LogWarning("Polymarket 并发限流：未获取到 Permit，已拒绝请求 {Method} {Uri}", request.Method, request.RequestUri);
            throw new PolymarketRateLimitRejectedException("Polymarket 并发限流：未获取到 Permit。");
        }

        // 2) 令牌桶
        using var tokenLease = await _tokenBucket!.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
        if (!tokenLease.IsAcquired)
        {
            _logger.LogWarning("Polymarket 令牌桶限流：无可用 token，已拒绝请求 {Method} {Uri}", request.Method, request.RequestUri);
            throw new PolymarketRateLimitRejectedException("Polymarket 令牌桶限流：无可用 token。");
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}

