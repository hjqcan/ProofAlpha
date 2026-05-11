// ============================================================================
// 可观测性配置选项
// ============================================================================

namespace Autotrade.Cli.Observability;

/// <summary>
/// 可观测性配置选项。
/// 控制追踪（Tracing）和指标（Metrics）的启用和导出方式。
/// </summary>
public sealed class ObservabilityOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "Observability";

    /// <summary>
    /// 是否启用分布式追踪（OpenTelemetry Tracing）。
    /// </summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>
    /// 是否启用指标收集（OpenTelemetry Metrics）。
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// 是否启用 OTLP 导出器（发送到 Jaeger/Tempo 等后端）。
    /// </summary>
    public bool EnableOtlpExporter { get; set; } = false;

    /// <summary>
    /// OTLP 导出器端点地址。
    /// 例如："http://localhost:4317"
    /// </summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>
    /// 是否启用 Prometheus 指标导出器。
    /// </summary>
    public bool EnablePrometheusExporter { get; set; } = false;

    /// <summary>
    /// Prometheus 指标监听端口。
    /// </summary>
    public int PrometheusPort { get; set; } = 9464;

    /// <summary>
    /// 服务名称（用于追踪和指标标识）。
    /// </summary>
    public string ServiceName { get; set; } = "Autotrade";
}
