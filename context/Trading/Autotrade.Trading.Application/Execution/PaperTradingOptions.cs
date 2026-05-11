namespace Autotrade.Trading.Application.Execution;

/// <summary>
/// 模拟交易配置选项。
/// </summary>
public sealed class PaperTradingOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "PaperTrading";

    /// <summary>
    /// Paper Trading 使用的虚拟钱包地址（用于创建/复用默认 TradingAccount）。
    /// </summary>
    public string WalletAddress { get; set; } = "paper";

    /// <summary>
    /// 滑点（基点，1 bps = 0.01%）。
    /// </summary>
    public int SlippageBps { get; set; } = 10;

    /// <summary>
    /// 部分成交概率（0.0 ~ 1.0）。
    /// 为 0 表示要么全部成交要么不成交。
    /// </summary>
    public double PartialFillProbability { get; set; } = 0.0;

    /// <summary>
    /// 模拟延迟（毫秒）。
    /// </summary>
    public int SimulatedLatencyMs { get; set; } = 50;

    /// <summary>
    /// 确定性随机种子（用于测试）。
    /// 为 null 时使用随机种子。
    /// </summary>
    public int? DeterministicSeed { get; set; }

    /// <summary>
    /// 默认成交率（当无法从订单簿确定时）。
    /// </summary>
    public double DefaultFillRate { get; set; } = 1.0;

    /// <summary>
    /// 部分成交时的最小成交比例。
    /// </summary>
    public double MinPartialFillRatio { get; set; } = 0.1;

    /// <summary>
    /// 部分成交时的最大成交比例。
    /// </summary>
    public double MaxPartialFillRatio { get; set; } = 0.9;
}
