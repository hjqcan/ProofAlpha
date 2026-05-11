namespace Autotrade.Polymarket.Options;

/// <summary>
/// HTTP 弹性策略配置（重试/断路器等）。
/// </summary>
public sealed class PolymarketResilienceOptions
{
    public const string SectionName = "Polymarket:Resilience";

    /// <summary>
    /// 最大重试次数（仅用于幂等请求）。
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// 基础重试延迟（指数退避的 base）。
    /// </summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 是否启用抖动（jitter），避免雪崩重试。
    /// </summary>
    public bool UseJitter { get; set; } = true;

    /// <summary>
    /// 断路器：采样窗口。
    /// </summary>
    public TimeSpan CircuitSamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 断路器：最小吞吐量（窗口内请求数不足则不触发）。
    /// </summary>
    public int CircuitMinimumThroughput { get; set; } = 20;

    /// <summary>
    /// 断路器：失败率阈值（0~1）。
    /// </summary>
    public double CircuitFailureRatio { get; set; } = 0.5;

    /// <summary>
    /// 断路器：打开后保持时间。
    /// </summary>
    public TimeSpan CircuitBreakDuration { get; set; } = TimeSpan.FromSeconds(30);
}

