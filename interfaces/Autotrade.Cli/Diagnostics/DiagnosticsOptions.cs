// ============================================================================
// Diagnostics 配置选项
// ============================================================================
// 诊断服务阈值配置。
// ============================================================================

namespace Autotrade.Cli.Diagnostics;

/// <summary>
/// 诊断服务配置选项。
/// </summary>
public sealed class DiagnosticsOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "Diagnostics";

    /// <summary>
    /// 是否启用诊断服务。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 诊断检查间隔（秒）。
    /// </summary>
    public int CheckIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// API 延迟警告阈值（毫秒）。
    /// </summary>
    public int ApiLatencyWarningMs { get; set; } = 500;

    /// <summary>
    /// API 延迟临界阈值（毫秒）。
    /// </summary>
    public int ApiLatencyCriticalMs { get; set; } = 2000;

    /// <summary>
    /// WebSocket 心跳警告阈值（秒）。
    /// </summary>
    public int WsHeartbeatWarningSeconds { get; set; } = 30;

    /// <summary>
    /// WebSocket 心跳临界阈值（秒）。
    /// </summary>
    public int WsHeartbeatCriticalSeconds { get; set; } = 60;

    /// <summary>
    /// 策略 lag 警告阈值（秒）。
    /// </summary>
    public int StrategyLagWarningSeconds { get; set; } = 10;

    /// <summary>
    /// 策略 lag 临界阈值（秒）。
    /// </summary>
    public int StrategyLagCriticalSeconds { get; set; } = 30;

    /// <summary>
    /// 错误率警告阈值（百分比）。
    /// </summary>
    public double ErrorRateWarningPercent { get; set; } = 5.0;

    /// <summary>
    /// 错误率临界阈值（百分比）。
    /// </summary>
    public double ErrorRateCriticalPercent { get; set; } = 20.0;
}
