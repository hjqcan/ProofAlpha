// ============================================================================
// 策略决策日志仓储接口
// ============================================================================

using Autotrade.Strategy.Domain.Entities;

namespace Autotrade.Strategy.Application.Decisions;

/// <summary>
/// 策略决策日志仓储接口。
/// </summary>
public interface IStrategyDecisionRepository
{
    /// <summary>
    /// 添加决策日志。
    /// </summary>
    Task AddAsync(StrategyDecisionLog log, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询决策日志。
    /// </summary>
    Task<IReadOnlyList<StrategyDecisionLog>> QueryAsync(StrategyDecisionQuery query,
        CancellationToken cancellationToken = default);

    Task<StrategyDecisionLog?> GetAsync(Guid decisionId, CancellationToken cancellationToken = default);
}
