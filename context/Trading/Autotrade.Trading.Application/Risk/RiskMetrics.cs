// ============================================================================
// 风险模块指标
// ============================================================================
// 使用 OpenTelemetry Metrics 收集风险相关指标：
// - Counters: 拒绝次数、Kill Switch 激活、订单错误、超时等
// - Gauges: 资本利用率、未对冲敞口数、活跃 Kill Switch 数
// - Histograms: 订单验证耗时
// ============================================================================

using System.Diagnostics.Metrics;
using Autotrade.Trading.Application.Contract.Risk;

namespace Autotrade.Trading.Application.Risk;

/// <summary>
/// 风险模块指标收集器。
/// </summary>
public sealed class RiskMetrics : IDisposable
{
    private readonly Meter _meter = new("Autotrade.Risk", "1.0.0");
    private readonly Counter<long> _rejections;
    private readonly Counter<long> _killSwitchActivations;
    private readonly Counter<long> _orderErrors;
    private readonly Counter<long> _postTradeChecks;
    private readonly Counter<long> _unhedgedExposureTimeouts;
    private readonly Counter<long> _exitActionsExecuted;
    private readonly ObservableGauge<double> _capitalUtilization;
    private readonly ObservableGauge<double> _totalOpenNotional;
    private readonly ObservableGauge<int> _totalOpenOrders;
    private readonly ObservableGauge<int> _activeKillSwitches;
    private readonly ObservableGauge<int> _unhedgedExposureCount;
    private readonly Histogram<double> _orderValidationDuration;

    private readonly object _lock = new();
    private double _currentCapitalUtilization;
    private double _currentTotalOpenNotional;
    private int _currentTotalOpenOrders;
    private int _currentActiveKillSwitches;
    private int _currentUnhedgedExposureCount;

    public RiskMetrics()
    {
        _rejections = _meter.CreateCounter<long>("risk.rejections", description: "风控拒绝次数");
        _killSwitchActivations = _meter.CreateCounter<long>("risk.killswitch.activations", description: "Kill Switch 激活次数");
        _orderErrors = _meter.CreateCounter<long>("risk.order.errors", description: "订单错误次数");
        _postTradeChecks = _meter.CreateCounter<long>("risk.posttrade.checks", description: "Post-trade 风险检查次数");
        _unhedgedExposureTimeouts = _meter.CreateCounter<long>("risk.unhedged.timeouts", description: "未对冲敞口超时次数");
        _exitActionsExecuted = _meter.CreateCounter<long>("risk.exit.actions", description: "退出动作执行次数");

        _capitalUtilization = _meter.CreateObservableGauge<double>(
            "risk.capital.utilization",
            () => GetLockedValue(() => _currentCapitalUtilization),
            unit: "percent",
            description: "资本利用率（0-1）");

        _totalOpenNotional = _meter.CreateObservableGauge<double>(
            "risk.notional.open",
            () => GetLockedValue(() => _currentTotalOpenNotional),
            unit: "USD",
            description: "当前未完成订单名义金额");

        _totalOpenOrders = _meter.CreateObservableGauge<int>(
            "risk.orders.open",
            () => GetLockedValue(() => _currentTotalOpenOrders),
            description: "当前未完成订单数");

        _activeKillSwitches = _meter.CreateObservableGauge<int>(
            "risk.killswitch.active",
            () => GetLockedValue(() => _currentActiveKillSwitches),
            description: "活跃的 Kill Switch 数量");

        _unhedgedExposureCount = _meter.CreateObservableGauge<int>(
            "risk.unhedged.count",
            () => GetLockedValue(() => _currentUnhedgedExposureCount),
            description: "当前未对冲敞口数量");

        _orderValidationDuration = _meter.CreateHistogram<double>(
            "risk.validation.duration",
            unit: "ms",
            description: "订单验证耗时");
    }

    private T GetLockedValue<T>(Func<T> getter)
    {
        lock (_lock)
        {
            return getter();
        }
    }

    public void IncrementRejections(string code)
    {
        _rejections.Add(1, new KeyValuePair<string, object?>("code", code));
    }

    public void IncrementKillSwitchActivations(KillSwitchLevel level, string reasonCode)
    {
        _killSwitchActivations.Add(1,
            new KeyValuePair<string, object?>("level", level.ToString()),
            new KeyValuePair<string, object?>("reason_code", reasonCode));
    }

    public void IncrementOrderErrors(string strategyId)
    {
        _orderErrors.Add(1, new KeyValuePair<string, object?>("strategy", strategyId));
    }

    public void IncrementPostTradeChecks(string strategyId, bool triggered)
    {
        _postTradeChecks.Add(1,
            new KeyValuePair<string, object?>("strategy", strategyId),
            new KeyValuePair<string, object?>("triggered", triggered));
    }

    public void IncrementUnhedgedExposureTimeouts(string strategyId, string marketId)
    {
        _unhedgedExposureTimeouts.Add(1,
            new KeyValuePair<string, object?>("strategy", strategyId),
            new KeyValuePair<string, object?>("has_market", !string.IsNullOrEmpty(marketId)));
    }

    public void IncrementExitActions(UnhedgedExitAction action, string strategyId)
    {
        _exitActionsExecuted.Add(1,
            new KeyValuePair<string, object?>("action", action.ToString()),
            new KeyValuePair<string, object?>("strategy", strategyId));
    }

    public void RecordOrderValidationDuration(double durationMs, string strategyId)
    {
        _orderValidationDuration.Record(durationMs,
            new KeyValuePair<string, object?>("strategy", strategyId));
    }

    /// <summary>
    /// 更新实时指标值（由 RiskManager 定期调用）。
    /// </summary>
    public void UpdateGauges(
        double capitalUtilization,
        double totalOpenNotional,
        int totalOpenOrders,
        int activeKillSwitches,
        int unhedgedExposureCount)
    {
        lock (_lock)
        {
            _currentCapitalUtilization = capitalUtilization;
            _currentTotalOpenNotional = totalOpenNotional;
            _currentTotalOpenOrders = totalOpenOrders;
            _currentActiveKillSwitches = activeKillSwitches;
            _currentUnhedgedExposureCount = unhedgedExposureCount;
        }
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
