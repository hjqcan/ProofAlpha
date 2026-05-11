// ============================================================================
// Trace Context Enricher
// ============================================================================
// 自动从 Activity.Current 提取 TraceId/SpanId 注入 Serilog 日志。
// ============================================================================

using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace Autotrade.Cli.Logging;

/// <summary>
/// Trace Context Enricher。
/// 将 OpenTelemetry 的 TraceId 和 SpanId 自动注入 Serilog 日志事件。
/// </summary>
public sealed class TraceContextEnricher : ILogEventEnricher
{
    /// <summary>
    /// 丰富日志事件，添加 TraceId 和 SpanId。
    /// </summary>
    /// <param name="logEvent">日志事件。</param>
    /// <param name="propertyFactory">属性工厂。</param>
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        // 添加 TraceId（如果不为空）
        if (activity.TraceId != default)
        {
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty("TraceId", activity.TraceId.ToString()));
        }

        // 添加 SpanId（如果不为空）
        if (activity.SpanId != default)
        {
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty("SpanId", activity.SpanId.ToString()));
        }

        // 添加 ParentSpanId（如果存在父级）
        if (activity.ParentSpanId != default)
        {
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty("ParentSpanId", activity.ParentSpanId.ToString()));
        }
    }
}
