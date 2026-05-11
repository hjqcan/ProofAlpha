namespace Autotrade.Polymarket.Options;

/// <summary>
/// 客户端侧限流配置（令牌桶 + 并发门）。
/// </summary>
public sealed class PolymarketRateLimitOptions
{
    public const string SectionName = "Polymarket:RateLimit";

    /// <summary>
    /// 每个补充周期发放的令牌数。
    /// </summary>
    public int TokensPerPeriod { get; set; } = 10;

    /// <summary>
    /// 补充周期长度。
    /// </summary>
    public TimeSpan ReplenishmentPeriod { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 桶容量（允许短时间 burst）。
    /// </summary>
    public int TokenBucketCapacity { get; set; } = 10;

    /// <summary>
    /// 同时进行的最大请求数（简单并发门）。
    /// </summary>
    public int MaxConcurrency { get; set; } = 8;

    /// <summary>
    /// 是否启用客户端限流（可用于测试时禁用）。
    /// </summary>
    public bool Enabled { get; set; } = true;
}

