namespace Autotrade.Polymarket.Options;

/// <summary>
/// Polymarket Gamma（市场元数据）API 配置。
/// </summary>
public sealed class PolymarketGammaOptions
{
    public const string SectionName = "Polymarket:Gamma";

    /// <summary>
    /// Gamma API Host（默认：https://gamma-api.polymarket.com）。
    /// </summary>
    public string Host { get; set; } = "https://gamma-api.polymarket.com";

    /// <summary>
    /// 请求超时。
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}

