using Autotrade.Trading.Application.Contract.Risk;

namespace Autotrade.Trading.Application.Risk;

/// <summary>
/// Kill Switch 控制面（跨进程）：通过配置文件声明期望的 Kill Switch 状态。
/// 运行中的进程会周期性对齐此期望状态。
/// </summary>
public sealed class KillSwitchControlOptions
{
    public const string SectionName = "RiskControl:KillSwitch";

    /// <summary>
    /// 是否期望全局 Kill Switch 处于激活状态。
    /// </summary>
    public bool GlobalActive { get; set; } = false;

    /// <summary>
    /// 全局 Kill Switch 级别（SoftStop/HardStop）。
    /// </summary>
    public KillSwitchLevel GlobalLevel { get; set; } = KillSwitchLevel.HardStop;

    public string GlobalReasonCode { get; set; } = "MANUAL";

    public string GlobalReason { get; set; } = "Manual kill switch";

    public string? GlobalContextJson { get; set; }

    /// <summary>
    /// 全局 Kill Switch 重置请求令牌（写入新值触发一次 reset）。
    /// </summary>
    public string? GlobalResetToken { get; set; }

    /// <summary>
    /// 策略级 Kill Switch 期望状态（key=StrategyId）。
    /// </summary>
    public Dictionary<string, StrategyKillSwitchControl> Strategies { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class StrategyKillSwitchControl
{
    public bool Active { get; set; } = false;

    public KillSwitchLevel Level { get; set; } = KillSwitchLevel.HardStop;

    public string ReasonCode { get; set; } = "MANUAL";

    public string Reason { get; set; } = "Manual strategy kill switch";

    public string? MarketId { get; set; }

    public string? ContextJson { get; set; }

    /// <summary>
    /// 策略级 Kill Switch 重置请求令牌（写入新值触发一次 reset）。
    /// </summary>
    public string? ResetToken { get; set; }
}

