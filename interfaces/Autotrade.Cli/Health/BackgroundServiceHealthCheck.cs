// ============================================================================
// 后台服务健康检查
// ============================================================================
// 检测关键 BackgroundService 心跳状态。
// ============================================================================

using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Engine;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Autotrade.Cli.Health;

/// <summary>
/// 后台服务健康检查配置选项。
/// </summary>
public sealed class BackgroundServiceHealthCheckOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "HealthChecks:BackgroundService";

    /// <summary>
    /// 心跳超时警告阈值（秒）。
    /// </summary>
    public int HeartbeatWarningSeconds { get; set; } = 30;

    /// <summary>
    /// 心跳超时临界阈值（秒）。
    /// </summary>
    public int HeartbeatCriticalSeconds { get; set; } = 60;
}

/// <summary>
/// 后台服务健康检查。
/// 检测策略引擎等关键后台服务的心跳状态。
/// </summary>
public sealed class BackgroundServiceHealthCheck : IHealthCheck
{
    private readonly IStrategyManager? _strategyManager;
    private readonly BackgroundServiceHealthCheckOptions _options;

    /// <summary>
    /// 初始化后台服务健康检查。
    /// </summary>
    /// <param name="strategyManager">策略管理器（可选）。</param>
    /// <param name="options">配置选项。</param>
    public BackgroundServiceHealthCheck(
        IStrategyManager? strategyManager = null,
        IOptions<BackgroundServiceHealthCheckOptions>? options = null)
    {
        _strategyManager = strategyManager;
        _options = options?.Value ?? new BackgroundServiceHealthCheckOptions();
    }

    /// <summary>
    /// 执行健康检查。
    /// </summary>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>();
        var issues = new List<string>();
        var warnings = new List<string>();
        var now = DateTimeOffset.UtcNow;

        if (_strategyManager is null)
        {
            data["strategy_engine"] = "N/A";
            return HealthCheckResult.Healthy("策略管理器未配置", data);
        }

        // 检查各策略的心跳
        var statuses = await _strategyManager.GetStatusesAsync(cancellationToken)
            .ConfigureAwait(false);

        data["strategy_count"] = statuses.Count;
        data["running_count"] = statuses.Count(s => s.State == StrategyState.Running);

        foreach (var status in statuses)
        {
            var strategyKey = $"strategy_{status.StrategyId}";
            
            if (status.State != StrategyState.Running)
            {
                data[$"{strategyKey}_state"] = status.State.ToString();
                continue;
            }

            if (status.LastHeartbeatUtc is null)
            {
                data[$"{strategyKey}_heartbeat"] = "never";
                warnings.Add($"策略 {status.StrategyId} 从未发送心跳");
                continue;
            }

            var heartbeatAge = (now - status.LastHeartbeatUtc.Value).TotalSeconds;
            data[$"{strategyKey}_heartbeat_age_seconds"] = (int)heartbeatAge;

            if (heartbeatAge >= _options.HeartbeatCriticalSeconds)
            {
                issues.Add($"策略 {status.StrategyId} 心跳超时 ({(int)heartbeatAge}s > {_options.HeartbeatCriticalSeconds}s)");
            }
            else if (heartbeatAge >= _options.HeartbeatWarningSeconds)
            {
                warnings.Add($"策略 {status.StrategyId} 心跳延迟 ({(int)heartbeatAge}s > {_options.HeartbeatWarningSeconds}s)");
            }
        }

        // 根据问题严重程度返回状态
        if (issues.Count > 0)
        {
            return HealthCheckResult.Unhealthy(
                string.Join("; ", issues),
                data: data);
        }

        if (warnings.Count > 0)
        {
            return HealthCheckResult.Degraded(
                string.Join("; ", warnings),
                data: data);
        }

        return HealthCheckResult.Healthy(
            $"所有后台服务正常 ({statuses.Count} 个策略)",
            data);
    }
}
