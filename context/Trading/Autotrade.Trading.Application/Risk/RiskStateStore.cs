// ============================================================================
// 风险状态存储
// ============================================================================
// 存储和管理风险系统的运行时状态，包括：
// - 活跃订单及其名义金额
// - 按策略/市场分组的资金占用统计
// - 未对冲敞口追踪
// - Kill Switch 状态（全局和策略级别）
// - 连续订单错误计数器
// 
// 所有状态操作都是线程安全的，使用锁机制保护。
// ============================================================================

using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;

namespace Autotrade.Trading.Application.Risk;

/// <summary>
/// 风险状态存储。
/// 线程安全地管理所有风险相关的运行时状态。
/// </summary>
public sealed class RiskStateStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, RiskOrderEntry> _orders = new();
    private readonly Dictionary<string, decimal> _openNotionalByStrategy = new();
    private readonly Dictionary<string, decimal> _openNotionalByMarket = new();
    private readonly Dictionary<(string StrategyId, string MarketId), decimal> _openNotionalByStrategyMarket = new();
    private readonly Dictionary<string, HashSet<string>> _marketsByStrategy = new();
    private readonly Dictionary<string, int> _openOrdersByStrategy = new();
    private readonly Dictionary<string, int> _consecutiveErrors = new();
    private readonly Dictionary<(string StrategyId, string MarketId), UnhedgedExposureSnapshot> _unhedged = new();
    private readonly Dictionary<string, KillSwitchState> _strategyKillSwitches = new();
    private decimal _totalOpenNotional;
    private int _totalOpenOrders;
    private KillSwitchState _globalKillSwitch = KillSwitchState.Inactive;

    public RiskSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new RiskSnapshot(
                _totalOpenNotional,
                _totalOpenOrders,
                new Dictionary<string, decimal>(_openNotionalByStrategy),
                new Dictionary<string, decimal>(_openNotionalByMarket),
                new Dictionary<(string, string), decimal>(_openNotionalByStrategyMarket),
                _marketsByStrategy.ToDictionary(kv => kv.Key, kv => new HashSet<string>(kv.Value)),
                new Dictionary<string, int>(_openOrdersByStrategy),
                new Dictionary<string, int>(_consecutiveErrors),
                _unhedged.Values.ToList());
        }
    }

    public IReadOnlyList<string> GetOpenOrderIds()
    {
        lock (_sync)
        {
            return _orders.Keys.ToList();
        }
    }

    public IReadOnlyList<string> GetOpenOrderIds(string strategyId)
    {
        lock (_sync)
        {
            return _orders
                .Where(kv => string.Equals(kv.Value.StrategyId, strategyId, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .ToList();
        }
    }

    public void AddOrder(RiskOrderEntry entry)
    {
        lock (_sync)
        {
            if (_orders.ContainsKey(entry.ClientOrderId))
            {
                return;
            }

            _orders[entry.ClientOrderId] = entry;
            _totalOpenOrders += 1;
            _totalOpenNotional += entry.OpenNotional;

            AddNotional(_openNotionalByStrategy, entry.StrategyId, entry.OpenNotional);
            AddNotional(_openNotionalByMarket, entry.MarketId, entry.OpenNotional);
            AddNotional(_openNotionalByStrategyMarket, (entry.StrategyId, entry.MarketId), entry.OpenNotional);

            if (!_marketsByStrategy.TryGetValue(entry.StrategyId, out var markets))
            {
                markets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _marketsByStrategy[entry.StrategyId] = markets;
            }

            markets.Add(entry.MarketId);

            if (_openOrdersByStrategy.ContainsKey(entry.StrategyId))
            {
                _openOrdersByStrategy[entry.StrategyId] += 1;
            }
            else
            {
                _openOrdersByStrategy[entry.StrategyId] = 1;
            }
        }
    }

    public void UpdateOrder(RiskOrderUpdate update)
    {
        lock (_sync)
        {
            if (!_orders.TryGetValue(update.ClientOrderId, out var entry))
            {
                return;
            }

            var isTerminalState = update.Status is ExecutionStatus.Filled
                or ExecutionStatus.Cancelled
                or ExecutionStatus.Rejected
                or ExecutionStatus.Expired;

            // 计算剩余名义金额
            var remainingQuantity = Math.Max(0m, entry.OriginalQuantity - update.FilledQuantity);
            var newNotional = remainingQuantity * entry.Price;

            // 对于终态订单，无论剩余多少都要清零
            if (isTerminalState)
            {
                newNotional = 0m;
            }

            var delta = newNotional - entry.OpenNotional;

            // 更新名义金额统计
            if (delta != 0m)
            {
                _totalOpenNotional = Math.Max(0m, _totalOpenNotional + delta);
                AddNotional(_openNotionalByStrategy, entry.StrategyId, delta);
                AddNotional(_openNotionalByMarket, entry.MarketId, delta);
                AddNotional(_openNotionalByStrategyMarket, (entry.StrategyId, entry.MarketId), delta);
            }

            if (isTerminalState)
            {
                // 终态：移除订单
                _orders.Remove(update.ClientOrderId);
                _totalOpenOrders = Math.Max(0, _totalOpenOrders - 1);

                if (_openOrdersByStrategy.TryGetValue(entry.StrategyId, out var count))
                {
                    _openOrdersByStrategy[entry.StrategyId] = Math.Max(0, count - 1);
                    if (_openOrdersByStrategy[entry.StrategyId] == 0)
                    {
                        _openOrdersByStrategy.Remove(entry.StrategyId);
                    }
                }

                // 清理零值的 notional 统计
                if (_openNotionalByStrategyMarket.TryGetValue((entry.StrategyId, entry.MarketId), out var smNotional) && smNotional <= 0m)
                {
                    _openNotionalByStrategyMarket.Remove((entry.StrategyId, entry.MarketId));
                }

                if (_openNotionalByStrategy.TryGetValue(entry.StrategyId, out var sNotional) && sNotional <= 0m)
                {
                    _openNotionalByStrategy.Remove(entry.StrategyId);
                }

                if (_openNotionalByMarket.TryGetValue(entry.MarketId, out var mNotional) && mNotional <= 0m)
                {
                    _openNotionalByMarket.Remove(entry.MarketId);
                }

                if (_marketsByStrategy.TryGetValue(entry.StrategyId, out var markets))
                {
                    if (!_openNotionalByStrategyMarket.ContainsKey((entry.StrategyId, entry.MarketId)))
                    {
                        markets.Remove(entry.MarketId);
                    }

                    if (markets.Count == 0)
                    {
                        _marketsByStrategy.Remove(entry.StrategyId);
                    }
                }
            }
            else
            {
                // 非终态：更新订单状态
                entry = entry with { OpenNotional = newNotional, FilledQuantity = update.FilledQuantity };
                _orders[update.ClientOrderId] = entry;
            }
        }
    }

    public RiskOrderEntry? GetOrder(string clientOrderId)
    {
        lock (_sync)
        {
            return _orders.TryGetValue(clientOrderId, out var entry) ? entry : null;
        }
    }

    public void RecordOrderError(string strategyId)
    {
        lock (_sync)
        {
            _consecutiveErrors.TryGetValue(strategyId, out var count);
            _consecutiveErrors[strategyId] = count + 1;
        }
    }

    public void ResetOrderErrors(string strategyId)
    {
        lock (_sync)
        {
            if (_consecutiveErrors.ContainsKey(strategyId))
            {
                _consecutiveErrors[strategyId] = 0;
            }
        }
    }

    public void RecordUnhedgedExposure(UnhedgedExposureSnapshot exposure)
    {
        lock (_sync)
        {
            _unhedged[(exposure.StrategyId, exposure.MarketId)] = exposure;
        }
    }

    public void ClearUnhedgedExposure(string strategyId, string marketId)
    {
        lock (_sync)
        {
            _unhedged.Remove((strategyId, marketId));
        }
    }

    #region Kill Switch State

    public KillSwitchState GetGlobalKillSwitch()
    {
        lock (_sync)
        {
            return _globalKillSwitch;
        }
    }

    public void SetGlobalKillSwitch(KillSwitchState state)
    {
        lock (_sync)
        {
            _globalKillSwitch = state;
        }
    }

    public KillSwitchState GetStrategyKillSwitch(string strategyId)
    {
        lock (_sync)
        {
            return _strategyKillSwitches.TryGetValue(strategyId, out var state)
                ? state
                : KillSwitchState.Inactive;
        }
    }

    public void SetStrategyKillSwitch(string strategyId, KillSwitchState state)
    {
        lock (_sync)
        {
            if (state.IsActive)
            {
                _strategyKillSwitches[strategyId] = state;
            }
            else
            {
                _strategyKillSwitches.Remove(strategyId);
            }
        }
    }

    public IReadOnlyList<KillSwitchState> GetAllActiveKillSwitches()
    {
        lock (_sync)
        {
            var result = new List<KillSwitchState>();
            if (_globalKillSwitch.IsActive)
            {
                result.Add(_globalKillSwitch);
            }

            result.AddRange(_strategyKillSwitches.Values.Where(s => s.IsActive));
            return result;
        }
    }

    public bool IsStrategyBlocked(string strategyId)
    {
        lock (_sync)
        {
            if (_globalKillSwitch.IsActive)
            {
                return true;
            }

            return _strategyKillSwitches.TryGetValue(strategyId, out var state) && state.IsActive;
        }
    }

    #endregion

    private static void AddNotional<TKey>(Dictionary<TKey, decimal> map, TKey key, decimal delta)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var current))
        {
            current = 0m;
        }

        current += delta;
        if (Math.Abs(current) < 0.0000001m)
        {
            map.Remove(key);
        }
        else
        {
            map[key] = current;
        }
    }
}

public sealed record RiskOrderEntry(
    string StrategyId,
    string ClientOrderId,
    string MarketId,
    string TokenId,
    decimal Price,
    decimal OriginalQuantity,
    decimal FilledQuantity,
    OrderLeg Leg,
    decimal OpenNotional,
    DateTimeOffset CreatedAtUtc);

public sealed record RiskSnapshot(
    decimal TotalOpenNotional,
    int TotalOpenOrders,
    IReadOnlyDictionary<string, decimal> OpenNotionalByStrategy,
    IReadOnlyDictionary<string, decimal> OpenNotionalByMarket,
    IReadOnlyDictionary<(string StrategyId, string MarketId), decimal> OpenNotionalByStrategyMarket,
    IReadOnlyDictionary<string, HashSet<string>> MarketsByStrategy,
    IReadOnlyDictionary<string, int> OpenOrdersByStrategy,
    IReadOnlyDictionary<string, int> ConsecutiveOrderErrors,
    IReadOnlyList<UnhedgedExposureSnapshot> UnhedgedExposures);
