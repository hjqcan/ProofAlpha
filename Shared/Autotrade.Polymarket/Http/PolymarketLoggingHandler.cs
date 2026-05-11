using System.Diagnostics;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace Autotrade.Polymarket.Http;

/// <summary>
/// Polymarket HTTP 管道日志：记录请求/响应耗时与状态码，并对敏感头脱敏。
/// </summary>
public sealed class PolymarketLoggingHandler : DelegatingHandler
{
    private static readonly HashSet<string> SensitiveHeaders =
    [
        PolymarketConstants.PolySignatureHeader,
        PolymarketConstants.PolyApiKeyHeader,
        PolymarketConstants.PolyPassphraseHeader
    ];

    private readonly ILogger<PolymarketLoggingHandler> _logger;

    public PolymarketLoggingHandler(ILogger<PolymarketLoggingHandler> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var correlationId = Activity.Current?.TraceId.ToString();

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Polymarket 请求：{Method} {Uri} CorrelationId={CorrelationId} Headers={Headers}",
                request.Method,
                request.RequestUri,
                correlationId,
                FormatHeaders(request.Headers));
        }

        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            sw.Stop();
            _logger.LogInformation(
                "Polymarket 响应：{StatusCode} {Method} {Uri} 耗时={ElapsedMs}ms CorrelationId={CorrelationId}",
                (int)response.StatusCode,
                request.Method,
                request.RequestUri,
                sw.ElapsedMilliseconds,
                correlationId);

            return response;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogWarning(
                ex,
                "Polymarket 请求异常：{Method} {Uri} 耗时={ElapsedMs}ms CorrelationId={CorrelationId}",
                request.Method,
                request.RequestUri,
                sw.ElapsedMilliseconds,
                correlationId);
            throw;
        }
    }

    private static string FormatHeaders(HttpRequestHeaders headers)
    {
        var pairs = new List<string>();
        foreach (var h in headers)
        {
            var value = SensitiveHeaders.Contains(h.Key) ? "***" : string.Join(",", h.Value);
            pairs.Add($"{h.Key}={value}");
        }

        return string.Join(";", pairs);
    }
}

