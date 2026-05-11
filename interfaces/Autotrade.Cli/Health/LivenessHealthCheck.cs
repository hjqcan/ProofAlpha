// ============================================================================
// 存活性健康检查
// ============================================================================

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Autotrade.Cli.Health;

/// <summary>
/// 存活性健康检查（Liveness Probe）。
/// 始终返回健康状态，表示进程正在运行。
/// 用于 K8s liveness probe，检测进程是否存活。
/// </summary>
public sealed class LivenessHealthCheck : IHealthCheck
{
    /// <summary>
    /// 执行健康检查。
    /// </summary>
    /// <param name="context">健康检查上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>始终返回健康状态。</returns>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HealthCheckResult.Healthy("服务存活"));
    }
}
