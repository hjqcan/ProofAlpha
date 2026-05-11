// ============================================================================
// WebSocket 连接健康检查
// ============================================================================
// 检测 CLOB/RTDS WebSocket 连接状态。
// ============================================================================

using Autotrade.MarketData.Application.WebSocket.Clob;
using Autotrade.MarketData.Application.WebSocket.Rtds;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Autotrade.Cli.Health;

/// <summary>
/// WebSocket 连接健康检查。
/// 检测 CLOB 和 RTDS WebSocket 连接状态。
/// </summary>
public sealed class WebSocketHealthCheck : IHealthCheck
{
    private readonly IClobMarketClient? _clobClient;
    private readonly IRtdsClient? _rtdsClient;

    /// <summary>
    /// 初始化 WebSocket 健康检查。
    /// </summary>
    /// <param name="clobClient">CLOB 市场客户端（可选）。</param>
    /// <param name="rtdsClient">RTDS 客户端（可选）。</param>
    public WebSocketHealthCheck(
        IClobMarketClient? clobClient = null,
        IRtdsClient? rtdsClient = null)
    {
        _clobClient = clobClient;
        _rtdsClient = rtdsClient;
    }

    /// <summary>
    /// 执行健康检查。
    /// </summary>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<string>();
        var data = new Dictionary<string, object>();

        // 检查 CLOB WebSocket
        if (_clobClient is not null)
        {
            var subscribedAssetCount = _clobClient.SubscribedAssets.Count;
            data["clob_connected"] = _clobClient.IsConnected;
            data["clob_state"] = _clobClient.State.ToString();
            data["clob_subscribed_assets"] = subscribedAssetCount;

            if (!_clobClient.IsConnected && subscribedAssetCount > 0)
            {
                issues.Add("CLOB WebSocket 未连接");
            }
        }
        else
        {
            data["clob_connected"] = "N/A";
        }

        // 检查 RTDS WebSocket
        if (_rtdsClient is not null)
        {
            data["rtds_connected"] = _rtdsClient.IsConnected;
            data["rtds_state"] = _rtdsClient.State.ToString();

            if (!_rtdsClient.IsConnected)
            {
                issues.Add("RTDS WebSocket 未连接");
            }
        }
        else
        {
            data["rtds_connected"] = "N/A";
        }

        // 如果没有客户端可检查，返回 Healthy
        if (_clobClient is null && _rtdsClient is null)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "WebSocket 客户端未配置",
                data));
        }

        // 根据问题数量返回不同状态
        if (issues.Count == 0)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "所有 WebSocket 连接正常",
                data));
        }

        if (issues.Count == 1)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                string.Join("; ", issues),
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Unhealthy(
            string.Join("; ", issues),
            data: data));
    }
}
