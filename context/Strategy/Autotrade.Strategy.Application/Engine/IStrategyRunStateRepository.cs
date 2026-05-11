// ============================================================================
// 策略运行状态仓储接口
// ============================================================================
// 持久化策略运行状态到数据库。
// ============================================================================

using Autotrade.Strategy.Application.Contract.Strategies;

namespace Autotrade.Strategy.Application.Engine;

/// <summary>
/// 策略运行状态仓储接口。
/// </summary>
public interface IStrategyRunStateRepository
{
    /// <summary>
    /// 插入或更新策略状态。
    /// </summary>
    Task UpsertAsync(StrategyStatus status, CancellationToken cancellationToken = default);

    Task SetDesiredStateAsync(string strategyId, StrategyState desiredState, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定策略的状态。
    /// </summary>
    Task<StrategyStatus?> GetAsync(string strategyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有策略的状态。
    /// </summary>
    Task<IReadOnlyList<StrategyStatus>> GetAllAsync(CancellationToken cancellationToken = default);
}
