namespace Autotrade.Polymarket.Options;

/// <summary>
/// Polymarket Data API 配置选项。
/// </summary>
public sealed class PolymarketDataOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "Polymarket:Data";

    /// <summary>
    /// Data API 主机地址。
    /// </summary>
    public string Host { get; set; } = "https://data-api.polymarket.com";

    /// <summary>
    /// 请求超时时间。
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}
