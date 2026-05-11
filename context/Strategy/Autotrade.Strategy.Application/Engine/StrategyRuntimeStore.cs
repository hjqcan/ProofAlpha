// ============================================================================
// 策略运行时状态存储
// ============================================================================
// 内存中存储所有策略的运行时状态，线程安全。
// ============================================================================

using Autotrade.Strategy.Application.Contract.Strategies;

namespace Autotrade.Strategy.Application.Engine;

/// <summary>
/// 策略运行时状态存储。
/// 内存中存储所有策略的运行时状态。
/// </summary>
public sealed class StrategyRuntimeStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, StrategyStatus> _states = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 初始化策略状态。
    /// </summary>
    public void Initialize(IEnumerable<StrategyDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        lock (_lock)
        {
            foreach (var descriptor in descriptors)
            {
                if (!_states.ContainsKey(descriptor.StrategyId))
                {
                    _states[descriptor.StrategyId] = new StrategyStatus(
                        descriptor.StrategyId,
                        descriptor.Name,
                        StrategyState.Created,
                        descriptor.Enabled,
                        descriptor.ConfigVersion,
                        0,
                        null,
                        null,
                        null,
                        null,
                        null,
                        0,
                        0,
                        0);
                }
            }
        }
    }

    /// <summary>
    /// 获取指定策略的状态。
    /// </summary>
    public StrategyStatus? GetStatus(string strategyId)
    {
        if (string.IsNullOrWhiteSpace(strategyId))
        {
            return null;
        }

        lock (_lock)
        {
            return _states.TryGetValue(strategyId, out var status) ? status : null;
        }
    }

    /// <summary>
    /// 获取所有策略的状态。
    /// </summary>
    public IReadOnlyList<StrategyStatus> GetAllStatuses()
    {
        lock (_lock)
        {
            return _states.Values
                .OrderBy(s => s.StrategyId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    /// <summary>
    /// 更新策略状态。
    /// </summary>
    public void UpdateStatus(StrategyStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        lock (_lock)
        {
            _states[status.StrategyId] = status;
        }
    }
}
