using System.Diagnostics.Metrics;

namespace Autotrade.Strategy.Application.Observability;

public static class StrategyMetrics
{
    private static readonly Meter Meter = new("Autotrade.Strategy", "1.0.0");

    public static readonly Counter<long> Decisions = Meter.CreateCounter<long>(
        "strategy.decisions",
        unit: "decisions",
        description: "Number of strategy decisions evaluated");

    public static readonly Counter<long> Signals = Meter.CreateCounter<long>(
        "strategy.signals",
        unit: "signals",
        description: "Number of strategy signals emitted");

    public static readonly Counter<long> OrdersPlaced = Meter.CreateCounter<long>(
        "strategy.orders.placed",
        unit: "orders",
        description: "Number of orders placed by strategies");

    public static readonly Counter<long> OrderFailures = Meter.CreateCounter<long>(
        "strategy.orders.failed",
        unit: "orders",
        description: "Number of order placements failed");

    public static readonly Counter<long> Errors = Meter.CreateCounter<long>(
        "strategy.errors",
        unit: "errors",
        description: "Number of strategy execution errors");

    public static readonly Counter<long> Restarts = Meter.CreateCounter<long>(
        "strategy.restarts",
        unit: "restarts",
        description: "Number of strategy restarts");

    public static readonly Histogram<double> HedgeTimeSeconds = Meter.CreateHistogram<double>(
        "strategy.hedge_time_seconds",
        unit: "s",
        description: "Time between first and second leg fills");

    /// <summary>
    /// 决策延迟：从快照时间戳到产生决策/信号的延迟。
    /// </summary>
    public static readonly Histogram<double> DecisionLatencyMs = Meter.CreateHistogram<double>(
        "strategy.decision_latency_ms",
        unit: "ms",
        description: "Latency from snapshot timestamp to decision emission");

    /// <summary>
    /// 策略评估周期耗时（不包含 sleep）。
    /// </summary>
    public static readonly Histogram<double> CycleDurationMs = Meter.CreateHistogram<double>(
        "strategy.cycle_duration_ms",
        unit: "ms",
        description: "Strategy evaluation cycle duration (work time)");

    /// <summary>
    /// 快照处理耗时（处理批量 snapshots）。
    /// </summary>
    public static readonly Histogram<double> SnapshotProcessingDurationMs = Meter.CreateHistogram<double>(
        "strategy.snapshot_processing_duration_ms",
        unit: "ms",
        description: "Time spent processing snapshots");

    public static void RecordDecision(string strategyId, string decisionType)
    {
        Decisions.Add(1, new KeyValuePair<string, object?>("strategy_id", strategyId),
            new KeyValuePair<string, object?>("type", decisionType));
    }

    public static void RecordSignal(string strategyId, string signalType)
    {
        Signals.Add(1, new KeyValuePair<string, object?>("strategy_id", strategyId),
            new KeyValuePair<string, object?>("type", signalType));
    }

    public static void RecordOrderPlaced(string strategyId)
    {
        OrdersPlaced.Add(1, new KeyValuePair<string, object?>("strategy_id", strategyId));
    }

    public static void RecordOrderFailure(string strategyId, string reason)
    {
        OrderFailures.Add(1, new KeyValuePair<string, object?>("strategy_id", strategyId),
            new KeyValuePair<string, object?>("reason", reason));
    }

    public static void RecordError(string strategyId, string type)
    {
        Errors.Add(1, new KeyValuePair<string, object?>("strategy_id", strategyId),
            new KeyValuePair<string, object?>("type", type));
    }

    public static void RecordRestart(string strategyId)
    {
        Restarts.Add(1, new KeyValuePair<string, object?>("strategy_id", strategyId));
    }

    public static void RecordHedgeTime(string strategyId, double seconds)
    {
        HedgeTimeSeconds.Record(seconds, new KeyValuePair<string, object?>("strategy_id", strategyId));
    }

    public static void RecordDecisionLatency(string strategyId, double latencyMs)
    {
        DecisionLatencyMs.Record(latencyMs, new KeyValuePair<string, object?>("strategy_id", strategyId));
    }

    public static void RecordCycleDuration(string strategyId, double durationMs)
    {
        CycleDurationMs.Record(durationMs, new KeyValuePair<string, object?>("strategy_id", strategyId));
    }

    public static void RecordSnapshotProcessingDuration(string strategyId, double durationMs)
    {
        SnapshotProcessingDurationMs.Record(durationMs, new KeyValuePair<string, object?>("strategy_id", strategyId));
    }
}
