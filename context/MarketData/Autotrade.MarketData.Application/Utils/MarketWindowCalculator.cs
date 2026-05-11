namespace Autotrade.MarketData.Application.Utils;

/// <summary>
/// 15 分钟市场窗口计算器。
/// 参考 polymarket-orderbook-watcher/source/utils.js 实现。
/// </summary>
public static class MarketWindowCalculator
{
    /// <summary>
    /// 15 分钟窗口的秒数。
    /// </summary>
    public const long WindowSeconds = 15 * 60; // 900 seconds

    /// <summary>
    /// 获取当前 15 分钟窗口的起始时间戳（Unix 秒）。
    /// </summary>
    public static long GetCurrent15MinWindowTimestamp()
    {
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return (nowSeconds / WindowSeconds) * WindowSeconds;
    }

    /// <summary>
    /// 获取指定时间的 15 分钟窗口起始时间戳。
    /// </summary>
    public static long Get15MinWindowTimestamp(DateTimeOffset time)
    {
        var seconds = time.ToUnixTimeSeconds();
        return (seconds / WindowSeconds) * WindowSeconds;
    }

    /// <summary>
    /// 获取当前窗口的起始时间。
    /// </summary>
    public static DateTimeOffset GetCurrentWindowStart()
    {
        var timestamp = GetCurrent15MinWindowTimestamp();
        return DateTimeOffset.FromUnixTimeSeconds(timestamp);
    }

    /// <summary>
    /// 获取当前窗口的结束时间。
    /// </summary>
    public static DateTimeOffset GetCurrentWindowEnd()
    {
        var timestamp = GetCurrent15MinWindowTimestamp();
        return DateTimeOffset.FromUnixTimeSeconds(timestamp + WindowSeconds);
    }

    /// <summary>
    /// 获取当前窗口内剩余秒数。
    /// </summary>
    public static long GetSecondsRemainingInWindow()
    {
        var windowEnd = GetCurrentWindowEnd();
        var remaining = (windowEnd - DateTimeOffset.UtcNow).TotalSeconds;
        return Math.Max(0, (long)remaining);
    }

    /// <summary>
    /// 构建 BTC Up/Down 15 分钟市场的 Slug。
    /// </summary>
    /// <param name="timestamp">窗口起始时间戳（Unix 秒）。</param>
    /// <returns>市场 Slug。</returns>
    public static string BuildMarketSlug(long timestamp)
    {
        return $"btc-updown-15m-{timestamp}";
    }

    /// <summary>
    /// 构建当前窗口的 BTC Up/Down 市场 Slug。
    /// </summary>
    public static string BuildCurrentMarketSlug()
    {
        return BuildMarketSlug(GetCurrent15MinWindowTimestamp());
    }

    /// <summary>
    /// 构建 Gamma API 的市场 URL。
    /// </summary>
    /// <param name="slug">市场 Slug。</param>
    /// <returns>API URL。</returns>
    public static string BuildGammaApiUrl(string slug)
    {
        return $"https://gamma-api.polymarket.com/events/slug/{slug}";
    }

    /// <summary>
    /// 构建当前窗口的 Gamma API URL。
    /// </summary>
    public static string BuildCurrentGammaApiUrl()
    {
        return BuildGammaApiUrl(BuildCurrentMarketSlug());
    }

    /// <summary>
    /// 检查当前是否在窗口的"安全"交易区间内（窗口开始后一段时间）。
    /// 参考策略：等待 5 分钟后再交易，确保现货价格已经确定。
    /// </summary>
    /// <param name="minSecondsAfterStart">窗口开始后的最小秒数。</param>
    /// <returns>是否在安全交易区间。</returns>
    public static bool IsInSafeTradingWindow(int minSecondsAfterStart = 300) // 默认 5 分钟
    {
        var windowStart = GetCurrentWindowStart();
        var elapsed = (DateTimeOffset.UtcNow - windowStart).TotalSeconds;
        return elapsed >= minSecondsAfterStart;
    }

    /// <summary>
    /// 获取窗口信息。
    /// </summary>
    public static MarketWindowInfo GetCurrentWindowInfo()
    {
        var start = GetCurrentWindowStart();
        var end = GetCurrentWindowEnd();
        var now = DateTimeOffset.UtcNow;

        return new MarketWindowInfo(
            StartUtc: start,
            EndUtc: end,
            Slug: BuildCurrentMarketSlug(),
            ElapsedSeconds: (long)(now - start).TotalSeconds,
            RemainingSeconds: (long)(end - now).TotalSeconds,
            IsInSafeTradingWindow: IsInSafeTradingWindow());
    }
}

/// <summary>
/// 市场窗口信息。
/// </summary>
public sealed record MarketWindowInfo(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string Slug,
    long ElapsedSeconds,
    long RemainingSeconds,
    bool IsInSafeTradingWindow);
