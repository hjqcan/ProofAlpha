using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace Autotrade.Cli.Logging;

/// <summary>
/// 关联 ID 注入器：
/// - 优先使用 Activity.Current.Id（与 grukirbs 的 CorrelationId 生成方式一致）
/// - 如果没有 Activity，则退化为一个临时 ID（仍保证字段存在）
/// </summary>
public sealed class CorrelationIdEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString();
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("CorrelationId", correlationId));
    }
}

