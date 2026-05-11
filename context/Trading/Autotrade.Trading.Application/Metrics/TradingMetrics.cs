// ============================================================================
// Trading 指标
// ============================================================================
// 订单吞吐量、成交率、延迟等核心 Trading 指标。
// ============================================================================

using System.Diagnostics.Metrics;

namespace Autotrade.Trading.Application.Metrics;

/// <summary>
/// Trading 模块指标定义与记录（OpenTelemetry Metrics）。
/// 重要：避免高基数标签（不使用 marketId/clientOrderId 等）。
/// </summary>
public static class TradingMetrics
{
    private static readonly Meter Meter = new("Autotrade.Trading", "1.0.0");

    public static readonly Counter<long> OrdersSubmitted = Meter.CreateCounter<long>(
        "trading.orders.submitted",
        unit: "orders",
        description: "Number of new order submissions (after idempotency/validation)");

    public static readonly Counter<long> OrdersAccepted = Meter.CreateCounter<long>(
        "trading.orders.accepted",
        unit: "orders",
        description: "Number of orders accepted by execution layer");

    public static readonly Counter<long> OrdersRejected = Meter.CreateCounter<long>(
        "trading.orders.rejected",
        unit: "orders",
        description: "Number of orders rejected (client-side or exchange)");

    public static readonly Counter<long> OrdersCancelled = Meter.CreateCounter<long>(
        "trading.orders.cancelled",
        unit: "orders",
        description: "Number of orders cancelled");

    public static readonly Counter<long> OrdersExpired = Meter.CreateCounter<long>(
        "trading.orders.expired",
        unit: "orders",
        description: "Number of orders expired");

    public static readonly Counter<long> OrdersFilled = Meter.CreateCounter<long>(
        "trading.orders.filled",
        unit: "fills",
        description: "Number of order fill events observed");

    public static readonly Counter<long> TradesExecuted = Meter.CreateCounter<long>(
        "trading.trades.executed",
        unit: "trades",
        description: "Number of trades executed");

    public static readonly Histogram<double> PlaceOrderLatencyMs = Meter.CreateHistogram<double>(
        "trading.orders.place_latency_ms",
        unit: "ms",
        description: "Latency of place-order call (end-to-end in execution service)");

    public static readonly Histogram<double> FillLatencyMs = Meter.CreateHistogram<double>(
        "trading.orders.fill_latency_ms",
        unit: "ms",
        description: "Latency from order creation to first fill observed (when available)");

    public static void RecordOrderSubmitted(string? strategyId, string executionMode)
        => OrdersSubmitted.Add(1, ExecutionModeTag(executionMode), StrategyTag(strategyId));

    public static void RecordOrderAccepted(string? strategyId, string executionMode)
        => OrdersAccepted.Add(1, ExecutionModeTag(executionMode), StrategyTag(strategyId));

    public static void RecordOrderRejected(string? strategyId, string executionMode, string? reasonCode)
    {
        OrdersRejected.Add(1,
            ExecutionModeTag(executionMode),
            StrategyTag(strategyId),
            new KeyValuePair<string, object?>("reason_code", NormalizeReasonCode(reasonCode)));
    }

    public static void RecordOrderCancelled(string? strategyId, string executionMode)
        => OrdersCancelled.Add(1, ExecutionModeTag(executionMode), StrategyTag(strategyId));

    public static void RecordOrderExpired(string? strategyId, string executionMode)
        => OrdersExpired.Add(1, ExecutionModeTag(executionMode), StrategyTag(strategyId));

    public static void RecordOrderFilled(string? strategyId, string executionMode, bool isPartial)
    {
        OrdersFilled.Add(1,
            ExecutionModeTag(executionMode),
            StrategyTag(strategyId),
            new KeyValuePair<string, object?>("fill_type", isPartial ? "partial" : "full"));
    }

    public static void RecordTradeExecuted(string? strategyId, string executionMode, string? side)
    {
        TradesExecuted.Add(1,
            ExecutionModeTag(executionMode),
            StrategyTag(strategyId),
            new KeyValuePair<string, object?>("side", NormalizeSide(side)));
    }

    public static void RecordPlaceOrderLatency(double latencyMs, string? strategyId, string executionMode, bool success)
    {
        PlaceOrderLatencyMs.Record(latencyMs,
            ExecutionModeTag(executionMode),
            StrategyTag(strategyId),
            new KeyValuePair<string, object?>("result", success ? "success" : "error"));
    }

    public static void RecordFillLatency(double latencyMs, string? strategyId, string executionMode, bool isPartial)
    {
        FillLatencyMs.Record(latencyMs,
            ExecutionModeTag(executionMode),
            StrategyTag(strategyId),
            new KeyValuePair<string, object?>("fill_type", isPartial ? "partial" : "full"));
    }

    private static KeyValuePair<string, object?> ExecutionModeTag(string executionMode)
        => new("execution_mode", string.IsNullOrWhiteSpace(executionMode) ? "unknown" : executionMode);

    private static KeyValuePair<string, object?> StrategyTag(string? strategyId)
        => new("strategy_id", string.IsNullOrWhiteSpace(strategyId) ? "unknown" : strategyId);

    private static string NormalizeReasonCode(string? reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            return "unknown";
        }

        var s = reasonCode.Trim();
        var cut = s.IndexOfAny([':', ' ', '\t', '\r', '\n']);
        if (cut > 0)
        {
            s = s[..cut];
        }

        // hard cap to avoid accidental high-cardinality strings
        return s.Length > 32 ? s[..32] : s;
    }

    private static string NormalizeSide(string? side)
    {
        if (string.IsNullOrWhiteSpace(side))
        {
            return "unknown";
        }

        var s = side.Trim();
        if (s.Equals("buy", StringComparison.OrdinalIgnoreCase)) return "buy";
        if (s.Equals("sell", StringComparison.OrdinalIgnoreCase)) return "sell";
        return "other";
    }
}
