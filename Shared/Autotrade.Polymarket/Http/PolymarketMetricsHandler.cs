using System.Diagnostics;
using Autotrade.Polymarket.Observability;
using Microsoft.Extensions.Logging;

namespace Autotrade.Polymarket.Http;

/// <summary>
/// HTTP 委托处理器，用于收集请求指标和追踪。
/// </summary>
public sealed class PolymarketMetricsHandler : DelegatingHandler
{
    private readonly PolymarketMetrics _metrics;
    private readonly ILogger<PolymarketMetricsHandler> _logger;

    public PolymarketMetricsHandler(PolymarketMetrics metrics, ILogger<PolymarketMetricsHandler> logger)
    {
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var method = request.Method.Method;
        var endpoint = request.RequestUri?.PathAndQuery ?? "/";

        using var activity = PolymarketActivitySource.StartHttpRequest(method, endpoint);
        activity?.SetTag("http.method", method);
        activity?.SetTag("http.url", request.RequestUri?.ToString());

        var sw = Stopwatch.StartNew();

        HttpResponseMessage? response = null;
        var statusCode = 0;
        var isSuccess = false;

        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            statusCode = (int)response.StatusCode;
            isSuccess = response.IsSuccessStatusCode;

            activity?.SetTag("http.status_code", statusCode);

            // 检测服务端 429
            if (statusCode == 429)
            {
                _metrics.RecordRateLimitHit(isClientSide: false);
            }

            return response;
        }
        catch (PolymarketRateLimitRejectedException)
        {
            // 客户端限流
            _metrics.RecordRateLimitHit(isClientSide: true);
            statusCode = 429;
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            statusCode = 0; // 网络错误
            _logger.LogWarning(ex, "Polymarket HTTP 请求异常：{Method} {Endpoint}", method, endpoint);
            throw;
        }
        finally
        {
            sw.Stop();
            _metrics.RecordRequest(method, endpoint, statusCode, sw.Elapsed.TotalMilliseconds, isSuccess);
        }
    }
}
