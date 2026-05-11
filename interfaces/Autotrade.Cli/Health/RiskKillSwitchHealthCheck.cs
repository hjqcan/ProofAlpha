// ============================================================================
// 风险 Kill Switch 健康检查
// ============================================================================

using Autotrade.Trading.Application.Contract.Risk;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Autotrade.Cli.Health;

/// <summary>
/// 风险 Kill Switch 健康检查。
/// 当全局或策略级 Kill Switch 激活时，返回不健康状态。
/// 用于 K8s readiness probe，确保交易系统处于正常状态。
/// </summary>
public sealed class RiskKillSwitchHealthCheck : IHealthCheck
{
    /// <summary>
    /// 风险管理器实例。
    /// </summary>
    private readonly IRiskManager _riskManager;

    /// <summary>
    /// 初始化风险 Kill Switch 健康检查。
    /// </summary>
    /// <param name="riskManager">风险管理器。</param>
    public RiskKillSwitchHealthCheck(IRiskManager riskManager)
    {
        _riskManager = riskManager ?? throw new ArgumentNullException(nameof(riskManager));
    }

    /// <summary>
    /// 执行健康检查。
    /// </summary>
    /// <param name="context">健康检查上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>健康 = Kill Switch 未激活，不健康 = Kill Switch 已激活。</returns>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (_riskManager.IsKillSwitchActive)
        {
            var state = _riskManager.GetKillSwitchState();
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Kill Switch 已激活: {state.Reason}"));
        }

        return Task.FromResult(HealthCheckResult.Healthy("Kill Switch 未激活"));
    }
}
