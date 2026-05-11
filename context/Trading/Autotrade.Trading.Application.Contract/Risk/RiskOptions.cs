using System.ComponentModel.DataAnnotations;

namespace Autotrade.Trading.Application.Contract.Risk;

/// <summary>
/// Risk management configuration.
/// </summary>
public sealed class RiskOptions : IValidatableObject
{
    public const string SectionName = "Risk";

    /// <summary>
    /// 单市场最大资金占用比例（0-1）。
    /// </summary>
    [Range(0.001, 1.0, ErrorMessage = "MaxCapitalPerMarket 必须在 0.001 到 1.0 之间")]
    public decimal MaxCapitalPerMarket { get; set; } = 0.05m;

    /// <summary>
    /// 单策略最大资金占用比例（0-1）。
    /// </summary>
    [Range(0.001, 1.0, ErrorMessage = "MaxCapitalPerStrategy 必须在 0.001 到 1.0 之间")]
    public decimal MaxCapitalPerStrategy { get; set; } = 0.30m;

    /// <summary>
    /// 全局最大资金使用比例（0-1）。
    /// </summary>
    [Range(0.001, 1.0, ErrorMessage = "MaxTotalCapitalUtilization 必须在 0.001 到 1.0 之间")]
    public decimal MaxTotalCapitalUtilization { get; set; } = 0.50m;

    /// <summary>
    /// 最大未完成订单数。
    /// </summary>
    [Range(1, 1000, ErrorMessage = "MaxOpenOrders 必须在 1 到 1000 之间")]
    public int MaxOpenOrders { get; set; } = 20;

    /// <summary>
    /// 单策略最大并发市场数。
    /// </summary>
    [Range(1, 100, ErrorMessage = "MaxConcurrentMarketsPerStrategy 必须在 1 到 100 之间")]
    public int MaxConcurrentMarketsPerStrategy { get; set; } = 3;

    /// <summary>
    /// 当日最大亏损熔断比例（0-1，可选）。
    /// </summary>
    [Range(0.001, 1.0, ErrorMessage = "DailyLossLimitPct 必须在 0.001 到 1.0 之间")]
    public decimal? DailyLossLimitPct { get; set; } = 0.02m;

    /// <summary>
    /// 连续下单失败熔断阈值（可选）。
    /// </summary>
    [Range(1, 100, ErrorMessage = "MaxConsecutiveOrderErrors 必须在 1 到 100 之间")]
    public int? MaxConsecutiveOrderErrors { get; set; } = 10;

    /// <summary>
    /// 单市场最大未对冲敞口比例（0-1）。
    /// </summary>
    [Range(0.001, 1.0, ErrorMessage = "MaxUnhedgedCapitalPerMarket 必须在 0.001 到 1.0 之间")]
    public decimal MaxUnhedgedCapitalPerMarket { get; set; } = 0.02m;

    /// <summary>
    /// 第一腿敞口最大暴露时间（秒）。
    /// </summary>
    [Range(1, 3600, ErrorMessage = "MaxFirstLegExposureSeconds 必须在 1 到 3600 之间")]
    public int MaxFirstLegExposureSeconds { get; set; } = 120;

    /// <summary>
    /// 是否启用 Kill Switch。
    /// </summary>
    public bool KillSwitchEnabled { get; set; } = true;

    /// <summary>
    /// 默认 Kill Switch 级别（当触发条件满足时使用）。
    /// </summary>
    public KillSwitchLevel DefaultKillSwitchLevel { get; set; } = KillSwitchLevel.HardStop;

    /// <summary>
    /// 未对冲敞口超时时的退出动作。
    /// </summary>
    public UnhedgedExitAction UnhedgedExitAction { get; set; } = UnhedgedExitAction.CancelAndExit;

    /// <summary>
    /// 是否在订单更新时执行 post-trade 风险检查。
    /// </summary>
    public bool EnablePostTradeRiskCheck { get; set; } = true;

    /// <summary>
    /// 资本利用率警告阈值（0-1）。超过此值记录警告日志。
    /// </summary>
    [Range(0.001, 1.0, ErrorMessage = "CapitalUtilizationWarningThreshold 必须在 0.001 到 1.0 之间")]
    public decimal CapitalUtilizationWarningThreshold { get; set; } = 0.40m;

    /// <summary>
    /// 执行配置校验。
    /// </summary>
    public void Validate()
    {
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(this, new ValidationContext(this), results, validateAllProperties: true))
        {
            var errors = string.Join("; ", results.Select(r => r.ErrorMessage));
            throw new InvalidOperationException($"RiskOptions 配置校验失败: {errors}");
        }

        // 额外的逻辑校验
        foreach (var result in ((IValidatableObject)this).Validate(new ValidationContext(this)))
        {
            if (result != ValidationResult.Success)
            {
                throw new InvalidOperationException($"RiskOptions 配置校验失败: {result.ErrorMessage}");
            }
        }
    }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // MaxCapitalPerMarket 应小于等于 MaxCapitalPerStrategy
        if (MaxCapitalPerMarket > MaxCapitalPerStrategy)
        {
            yield return new ValidationResult(
                "MaxCapitalPerMarket 不应大于 MaxCapitalPerStrategy",
                new[] { nameof(MaxCapitalPerMarket), nameof(MaxCapitalPerStrategy) });
        }

        // MaxCapitalPerStrategy 应小于等于 MaxTotalCapitalUtilization
        if (MaxCapitalPerStrategy > MaxTotalCapitalUtilization)
        {
            yield return new ValidationResult(
                "MaxCapitalPerStrategy 不应大于 MaxTotalCapitalUtilization",
                new[] { nameof(MaxCapitalPerStrategy), nameof(MaxTotalCapitalUtilization) });
        }

        // MaxUnhedgedCapitalPerMarket 应小于等于 MaxCapitalPerMarket
        if (MaxUnhedgedCapitalPerMarket > MaxCapitalPerMarket)
        {
            yield return new ValidationResult(
                "MaxUnhedgedCapitalPerMarket 不应大于 MaxCapitalPerMarket",
                new[] { nameof(MaxUnhedgedCapitalPerMarket), nameof(MaxCapitalPerMarket) });
        }

        // CapitalUtilizationWarningThreshold 应小于 MaxTotalCapitalUtilization
        if (CapitalUtilizationWarningThreshold >= MaxTotalCapitalUtilization)
        {
            yield return new ValidationResult(
                "CapitalUtilizationWarningThreshold 应小于 MaxTotalCapitalUtilization",
                new[] { nameof(CapitalUtilizationWarningThreshold), nameof(MaxTotalCapitalUtilization) });
        }
    }
}

/// <summary>
/// 未对冲敞口超时时的退出动作。
/// </summary>
public enum UnhedgedExitAction
{
    /// <summary>
    /// 仅记录日志和触发告警，不执行任何订单操作。
    /// </summary>
    LogOnly = 0,

    /// <summary>
    /// 撤销相关订单。
    /// </summary>
    CancelOrders = 1,

    /// <summary>
    /// 撤销订单并触发退出（市价卖出敞口头寸）。
    /// </summary>
    CancelAndExit = 2,

    /// <summary>
    /// 尝试以当前市价完成对冲（强制买入第二腿）。
    /// </summary>
    ForceHedge = 3
}
