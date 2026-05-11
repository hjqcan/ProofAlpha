// ============================================================================
// 尾盘扫货策略配置
// ============================================================================
// 配置尾盘扫货策略的各项参数，包括：
// - 市场筛选条件（距离结算时间、流动性等）
// - 入场条件（最小胜率、最大入场价、最小收益率）
// - 订单参数（名义价值限制、下单数量、冷却时间）
// ============================================================================

namespace Autotrade.Strategy.Application.Strategies.Endgame;

/// <summary>
/// 尾盘扫货策略配置。
/// 
/// 策略逻辑：
/// - 选择即将结算（如 15 分钟内）的市场
/// - 当某一方概率 >= MinWinProbability（如 90%）时入场
/// - 利用高胜率锁定小但几乎确定的利润
/// </summary>
public sealed class EndgameSweepOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "Strategies:EndgameSweep";

    /// <summary>
    /// 是否启用策略。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 配置版本，用于热更新检测。
    /// </summary>
    public string ConfigVersion { get; set; } = "v1";

    #region 市场筛选条件

    /// <summary>
    /// 距离结算的最大时间（秒），超过此时间不参与。
    /// 例如：900 = 15 分钟内结算的市场。
    /// </summary>
    public int MaxSecondsToExpiry { get; set; } = 900;

    /// <summary>
    /// 距离结算的最小时间（秒），太临近结算可能无法成交。
    /// 例如：60 = 至少还有 1 分钟。
    /// </summary>
    public int MinSecondsToExpiry { get; set; } = 60;

    /// <summary>
    /// 最小流动性要求。
    /// </summary>
    public decimal MinLiquidity { get; set; } = 500m;

    /// <summary>
    /// 最大同时参与的市场数量。
    /// </summary>
    public int MaxMarkets { get; set; } = 10;

    #endregion

    #region 入场条件

    /// <summary>
    /// 最小获胜概率阈值（基于盘口价格推算）。
    /// 例如：0.90 = 价格 >= 0.90 视为高胜率。
    /// </summary>
    public decimal MinWinProbability { get; set; } = 0.90m;

    /// <summary>
    /// 最大入场价格，避免买入价格过高导致利润过薄。
    /// 例如：0.98 = 买入价不超过 0.98。
    /// </summary>
    public decimal MaxEntryPrice { get; set; } = 0.98m;

    /// <summary>
    /// 最小预期收益率（1 - 买入价）。
    /// 例如：0.02 = 至少 2% 收益。
    /// </summary>
    public decimal MinExpectedProfitRate { get; set; } = 0.02m;

    #endregion

    #region 订单参数

    /// <summary>
    /// 每市场最大名义价值。
    /// </summary>
    public decimal MaxNotionalPerMarket { get; set; } = 50m;

    /// <summary>
    /// 每单最大名义价值。
    /// </summary>
    public decimal MaxNotionalPerOrder { get; set; } = 20m;

    /// <summary>
    /// 默认下单数量。
    /// </summary>
    public decimal DefaultOrderQuantity { get; set; } = 10m;

    /// <summary>
    /// 最小下单数量。
    /// </summary>
    public decimal MinOrderQuantity { get; set; } = 1m;

    /// <summary>
    /// 最大滑点容忍度。
    /// </summary>
    public decimal MaxSlippage { get; set; } = 0.01m;

    /// <summary>
    /// 入场冷却时间（秒），防止同一市场频繁交易。
    /// </summary>
    public int EntryCooldownSeconds { get; set; } = 30;

    #endregion

    /// <summary>
    /// 验证配置有效性。
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConfigVersion))
        {
            throw new ArgumentException("ConfigVersion cannot be empty.", nameof(ConfigVersion));
        }

        if (MaxSecondsToExpiry <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSecondsToExpiry), MaxSecondsToExpiry,
                "MaxSecondsToExpiry must be positive.");
        }

        if (MinSecondsToExpiry < 0 || MinSecondsToExpiry >= MaxSecondsToExpiry)
        {
            throw new ArgumentOutOfRangeException(nameof(MinSecondsToExpiry), MinSecondsToExpiry,
                "MinSecondsToExpiry must be non-negative and less than MaxSecondsToExpiry.");
        }

        if (MinWinProbability <= 0m || MinWinProbability >= 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(MinWinProbability), MinWinProbability,
                "MinWinProbability must be between 0 and 1.");
        }

        if (MaxEntryPrice <= 0m || MaxEntryPrice >= 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxEntryPrice), MaxEntryPrice,
                "MaxEntryPrice must be between 0 and 1.");
        }

        if (MinExpectedProfitRate < 0m || MinExpectedProfitRate >= 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(MinExpectedProfitRate), MinExpectedProfitRate,
                "MinExpectedProfitRate must be between 0 and 1.");
        }

        if (MaxNotionalPerMarket <= 0m || MaxNotionalPerOrder <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxNotionalPerMarket), MaxNotionalPerMarket,
                "Notional limits must be positive.");
        }

        if (DefaultOrderQuantity <= 0m || MinOrderQuantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(DefaultOrderQuantity), DefaultOrderQuantity,
                "Order quantities must be positive.");
        }

        if (MaxSlippage < 0m || MaxSlippage >= 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSlippage), MaxSlippage,
                "MaxSlippage must be between 0 and 1.");
        }

        if (EntryCooldownSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(EntryCooldownSeconds), EntryCooldownSeconds,
                "EntryCooldownSeconds must be non-negative.");
        }
    }
}
