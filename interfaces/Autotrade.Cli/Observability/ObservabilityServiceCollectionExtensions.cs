// ============================================================================
// 可观测性服务注册扩展
// ============================================================================

using Autotrade.MarketData.Application.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Autotrade.Cli.Observability;

/// <summary>
/// 可观测性服务注册扩展方法。
/// 配置 OpenTelemetry 追踪和指标收集。
/// </summary>
public static class ObservabilityServiceCollectionExtensions
{
    /// <summary>
    /// 添加可观测性服务（追踪和指标）。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置。</param>
    /// <returns>服务集合（链式调用）。</returns>
    public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ObservabilityOptions>(
            configuration.GetSection(ObservabilityOptions.SectionName));

        var options = configuration.GetSection(ObservabilityOptions.SectionName)
            .Get<ObservabilityOptions>() ?? new ObservabilityOptions();

        services.AddOpenTelemetry()
            .ConfigureResource(resource =>
            {
                resource.AddService(options.ServiceName);
            })
            .WithTracing(builder => ConfigureTracing(builder, options))
            .WithMetrics(builder => ConfigureMetrics(builder, options));

        return services;
    }

    /// <summary>
    /// 配置分布式追踪。
    /// </summary>
    private static void ConfigureTracing(TracerProviderBuilder builder, ObservabilityOptions options)
    {
        if (!options.EnableTracing)
        {
            return;
        }

        // 注册追踪源
        builder
            .AddSource("Autotrade")
            .AddSource(MarketDataActivitySource.SourceName)
            .AddSource("Autotrade.Polymarket")
            .AddSource("Autotrade.Trading");

        // 配置 OTLP 导出器（发送到 Jaeger/Tempo 等）
        if (options.EnableOtlpExporter && !string.IsNullOrWhiteSpace(options.OtlpEndpoint))
        {
            builder.AddOtlpExporter(o => o.Endpoint = new Uri(options.OtlpEndpoint));
        }
    }

    /// <summary>
    /// 配置指标收集。
    /// </summary>
    private static void ConfigureMetrics(MeterProviderBuilder builder, ObservabilityOptions options)
    {
        if (!options.EnableMetrics)
        {
            return;
        }

        // 注册各上下文的指标
        builder
            .AddMeter("Autotrade.MarketData")
            .AddMeter("Autotrade.Strategy")
            .AddMeter("Autotrade.Risk")
            .AddMeter("Autotrade.Polymarket")
            .AddMeter("Autotrade.Trading");

        // 配置 Prometheus HTTP 监听器（供 Prometheus 抓取）
        if (options.EnablePrometheusExporter)
        {
            var prefix = $"http://+:{options.PrometheusPort}/";
            builder.AddPrometheusHttpListener(o => o.UriPrefixes = new[] { prefix });
        }

        // 配置 OTLP 导出器
        if (options.EnableOtlpExporter && !string.IsNullOrWhiteSpace(options.OtlpEndpoint))
        {
            builder.AddOtlpExporter(o => o.Endpoint = new Uri(options.OtlpEndpoint));
        }
    }
}
