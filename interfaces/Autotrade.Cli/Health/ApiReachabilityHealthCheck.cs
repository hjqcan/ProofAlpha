// ============================================================================
// API 可达性健康检查
// ============================================================================
// 检测 Polymarket CLOB API 可达性和延迟。
// ============================================================================

using System.Diagnostics;
using Autotrade.Polymarket.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Autotrade.Cli.Health;

/// <summary>
/// API 可达性配置选项。
/// </summary>
public sealed class ApiHealthCheckOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "HealthChecks:Api";

    /// <summary>
    /// 延迟警告阈值（毫秒）。
    /// </summary>
    public int LatencyWarningMs { get; set; } = 500;

    /// <summary>
    /// 延迟临界阈值（毫秒）。
    /// </summary>
    public int LatencyCriticalMs { get; set; } = 2000;

    /// <summary>
    /// 请求超时（毫秒）。
    /// </summary>
    public int TimeoutMs { get; set; } = 5000;
}

/// <summary>
/// API 可达性健康检查。
/// 通过探测 CLOB API /time 端点检测可达性和延迟。
/// </summary>
public sealed class ApiReachabilityHealthCheck : IHealthCheck
{
    private readonly IPolymarketClobClient? _clobClient;
    private readonly ApiHealthCheckOptions _options;

    /// <summary>
    /// 初始化 API 可达性健康检查。
    /// </summary>
    /// <param name="clobClient">CLOB 客户端（可选）。</param>
    /// <param name="options">配置选项。</param>
    public ApiReachabilityHealthCheck(
        IPolymarketClobClient? clobClient = null,
        IOptions<ApiHealthCheckOptions>? options = null)
    {
        _clobClient = clobClient;
        _options = options?.Value ?? new ApiHealthCheckOptions();
    }

    /// <summary>
    /// 执行健康检查。
    /// </summary>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>();

        if (_clobClient is null)
        {
            data["api_available"] = "N/A";
            return HealthCheckResult.Healthy("CLOB 客户端未配置", data);
        }

        var sw = Stopwatch.StartNew();

        try
        {
            using var timeoutCts = new CancellationTokenSource(_options.TimeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            // 探测 /time 端点
            var serverTime = await _clobClient.GetServerTimeAsync(linkedCts.Token)
                .ConfigureAwait(false);

            sw.Stop();
            var latencyMs = sw.ElapsedMilliseconds;

            data["api_available"] = true;
            data["latency_ms"] = latencyMs;
            data["server_time"] = serverTime;

            // 根据延迟判断状态
            if (latencyMs >= _options.LatencyCriticalMs)
            {
                return HealthCheckResult.Unhealthy(
                    $"API 延迟过高: {latencyMs}ms (阈值: {_options.LatencyCriticalMs}ms)",
                    data: data);
            }

            if (latencyMs >= _options.LatencyWarningMs)
            {
                return HealthCheckResult.Degraded(
                    $"API 延迟偏高: {latencyMs}ms (警告阈值: {_options.LatencyWarningMs}ms)",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                $"API 响应正常: {latencyMs}ms",
                data);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            data["api_available"] = false;
            data["latency_ms"] = sw.ElapsedMilliseconds;
            data["error"] = "请求超时";

            return HealthCheckResult.Unhealthy(
                $"API 请求超时 (>{_options.TimeoutMs}ms)",
                data: data);
        }
        catch (Exception ex)
        {
            sw.Stop();
            data["api_available"] = false;
            data["latency_ms"] = sw.ElapsedMilliseconds;
            data["error"] = ex.Message;

            return HealthCheckResult.Unhealthy(
                $"API 不可达: {ex.Message}",
                exception: ex,
                data: data);
        }
    }
}
