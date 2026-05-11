namespace Autotrade.Trading.Application.Contract.Risk;

/// <summary>
/// Kill switch 触发级别。
/// </summary>
public enum KillSwitchLevel
{
    /// <summary>
    /// 未激活。
    /// </summary>
    None = 0,

    /// <summary>
    /// 软停止：停止新下单，保留现有订单。
    /// 适用于轻度风险（如临时限流、短暂网络问题）。
    /// </summary>
    SoftStop = 1,

    /// <summary>
    /// 硬停止：停止新下单 + 撤销所有现有订单 + 触发配置化退出动作。
    /// 适用于严重风险（如连续错误、敞口超时、日亏损限制）。
    /// </summary>
    HardStop = 2
}
