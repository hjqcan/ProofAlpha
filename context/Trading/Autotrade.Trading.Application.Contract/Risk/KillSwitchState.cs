namespace Autotrade.Trading.Application.Contract.Risk;

/// <summary>
/// Kill switch 状态快照。
/// 支持全局级别和策略级别的 SoftStop/HardStop 控制。
/// </summary>
public sealed record KillSwitchState
{
    /// <summary>
    /// 是否激活（任何级别的 kill switch）。
    /// </summary>
    public required bool IsActive { get; init; }

    /// <summary>
    /// Kill switch 级别。
    /// </summary>
    public KillSwitchLevel Level { get; init; } = KillSwitchLevel.None;

    /// <summary>
    /// 触发原因。
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// 触发原因代码（用于指标统计与告警匹配）。
    /// </summary>
    public string? ReasonCode { get; init; }

    /// <summary>
    /// 关联的策略 ID（为 null 表示全局 kill switch）。
    /// </summary>
    public string? StrategyId { get; init; }

    /// <summary>
    /// 关联的市场 ID（可选，用于定位具体问题市场）。
    /// </summary>
    public string? MarketId { get; init; }

    /// <summary>
    /// 激活时间。
    /// </summary>
    public DateTimeOffset? ActivatedAtUtc { get; init; }

    /// <summary>
    /// 触发时的上下文信息（JSON 格式，便于审计）。
    /// </summary>
    public string? ContextJson { get; init; }

    /// <summary>
    /// 创建未激活状态。
    /// </summary>
    public static KillSwitchState Inactive => new() { IsActive = false, Level = KillSwitchLevel.None };
}
