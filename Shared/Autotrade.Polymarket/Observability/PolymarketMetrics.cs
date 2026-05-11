using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Autotrade.Polymarket.Observability;

/// <summary>
/// Polymarket API 客户端可观测性指标。
/// </summary>
public sealed class PolymarketMetrics : IDisposable
{
    public const string MeterName = "Autotrade.Polymarket";

    private readonly Meter _meter;
    private readonly Counter<long> _requestsTotal;
    private readonly Counter<long> _requestsFailedTotal;
    private readonly Counter<long> _rateLimitHitsTotal;
    private readonly Counter<long> _circuitBreakerOpenTotal;
    private readonly Histogram<double> _requestDurationMs;

    public PolymarketMetrics(IMeterFactory? meterFactory = null)
    {
        _meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);

        _requestsTotal = _meter.CreateCounter<long>(
            "polymarket_requests_total",
            "requests",
            "Total number of requests made to Polymarket API");

        _requestsFailedTotal = _meter.CreateCounter<long>(
            "polymarket_requests_failed_total",
            "requests",
            "Total number of failed requests to Polymarket API");

        _rateLimitHitsTotal = _meter.CreateCounter<long>(
            "polymarket_rate_limit_hits_total",
            "hits",
            "Total number of rate limit hits (429 or client-side rejection)");

        _circuitBreakerOpenTotal = _meter.CreateCounter<long>(
            "polymarket_circuit_breaker_open_total",
            "events",
            "Total number of times circuit breaker opened");

        _requestDurationMs = _meter.CreateHistogram<double>(
            "polymarket_request_duration_ms",
            "ms",
            "Request duration in milliseconds");
    }

    /// <summary>
    /// 记录一个请求完成。
    /// </summary>
    public void RecordRequest(string method, string endpoint, int statusCode, double durationMs, bool isSuccess)
    {
        var tags = new TagList
        {
            { "method", method },
            { "endpoint", SanitizeEndpoint(endpoint) },
            { "status_code", statusCode.ToString() }
        };

        _requestsTotal.Add(1, tags);
        _requestDurationMs.Record(durationMs, tags);

        if (!isSuccess)
        {
            _requestsFailedTotal.Add(1, tags);
        }
    }

    /// <summary>
    /// 记录限流命中（429 或客户端拒绝）。
    /// </summary>
    public void RecordRateLimitHit(bool isClientSide)
    {
        var tags = new TagList
        {
            { "source", isClientSide ? "client" : "server" }
        };
        _rateLimitHitsTotal.Add(1, tags);
    }

    /// <summary>
    /// 记录断路器打开事件。
    /// </summary>
    public void RecordCircuitBreakerOpen()
    {
        _circuitBreakerOpenTotal.Add(1);
    }

    /// <summary>
    /// 对 endpoint 进行规范化处理，避免高基数（如 /markets/xxx 统一为 /markets/{id}）。
    /// </summary>
    private static string SanitizeEndpoint(string endpoint)
    {
        // 移除 querystring
        var idx = endpoint.IndexOf('?');
        var path = idx > 0 ? endpoint[..idx] : endpoint;

        // 常见模式规范化
        if (path.StartsWith("/markets/") && path.Length > "/markets/".Length)
        {
            return "/markets/{id}";
        }

        if (path.StartsWith("/data/order/") && path.Length > "/data/order/".Length)
        {
            return "/data/order/{id}";
        }

        return path;
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
