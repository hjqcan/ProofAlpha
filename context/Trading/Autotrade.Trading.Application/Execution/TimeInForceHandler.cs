using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Trading.Application.Execution;

/// <summary>
/// Time-in-Force 处理器：处理各种订单时效规则。
/// </summary>
public static class TimeInForceHandler
{
    /// <summary>
    /// 验证 Time-in-Force 参数。
    /// </summary>
    /// <param name="request">执行请求。</param>
    /// <returns>错误消息，验证通过返回 null。</returns>
    public static string? Validate(ExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.TimeInForce switch
        {
            TimeInForce.Gtd when request.GoodTilDateUtc is null =>
                "GTD 订单必须提供 GoodTilDateUtc",

            TimeInForce.Gtd when request.GoodTilDateUtc <= DateTimeOffset.UtcNow =>
                "GoodTilDateUtc 必须晚于当前时间",

            _ => null
        };
    }

    /// <summary>
    /// 判断未成交部分是否应该取消（FAK/FOK 逻辑）。
    /// </summary>
    /// <param name="timeInForce">订单时效。</param>
    /// <param name="filledQuantity">已成交数量。</param>
    /// <param name="totalQuantity">总数量。</param>
    /// <returns>是否应取消未成交部分。</returns>
    public static bool ShouldCancelRemaining(
        TimeInForce timeInForce,
        decimal filledQuantity,
        decimal totalQuantity)
    {
        return timeInForce switch
        {
            // FOK：必须全部成交，否则全部取消
            TimeInForce.Fok when filledQuantity < totalQuantity => true,

            // FAK：任何成交后取消剩余
            TimeInForce.Fak when filledQuantity > 0 && filledQuantity < totalQuantity => true,

            // FAK：无成交时也取消
            TimeInForce.Fak when filledQuantity == 0 => true,

            _ => false
        };
    }

    /// <summary>
    /// 判断订单是否已过期（GTD）。
    /// </summary>
    /// <param name="timeInForce">订单时效。</param>
    /// <param name="expirationUtc">到期时间。</param>
    /// <returns>是否已过期。</returns>
    public static bool IsExpired(TimeInForce timeInForce, DateTimeOffset? expirationUtc)
    {
        if (timeInForce != TimeInForce.Gtd)
        {
            return false;
        }

        if (expirationUtc is null)
        {
            return false;
        }

        return DateTimeOffset.UtcNow > expirationUtc.Value;
    }

    /// <summary>
    /// 计算距离过期的剩余时间。
    /// </summary>
    /// <param name="timeInForce">订单时效。</param>
    /// <param name="expirationUtc">到期时间。</param>
    /// <returns>剩余时间，非 GTD 或无过期时间返回 null。</returns>
    public static TimeSpan? GetTimeToExpiry(TimeInForce timeInForce, DateTimeOffset? expirationUtc)
    {
        if (timeInForce != TimeInForce.Gtd || expirationUtc is null)
        {
            return null;
        }

        var remaining = expirationUtc.Value - DateTimeOffset.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>
    /// 获取订单的有效执行状态。
    /// </summary>
    /// <param name="timeInForce">订单时效。</param>
    /// <param name="expirationUtc">到期时间。</param>
    /// <param name="filledQuantity">已成交数量。</param>
    /// <param name="totalQuantity">总数量。</param>
    /// <param name="currentStatus">当前状态。</param>
    /// <returns>调整后的状态。</returns>
    public static ExecutionStatus GetEffectiveStatus(
        TimeInForce timeInForce,
        DateTimeOffset? expirationUtc,
        decimal filledQuantity,
        decimal totalQuantity,
        ExecutionStatus currentStatus)
    {
        // 已终态不再调整
        if (currentStatus is ExecutionStatus.Filled or ExecutionStatus.Cancelled or
            ExecutionStatus.Rejected or ExecutionStatus.Expired)
        {
            return currentStatus;
        }

        // 检查过期
        if (IsExpired(timeInForce, expirationUtc))
        {
            return ExecutionStatus.Expired;
        }

        // 检查 FAK/FOK 逻辑
        if (ShouldCancelRemaining(timeInForce, filledQuantity, totalQuantity))
        {
            // FOK: 未全成交即全部取消（不允许部分成交）
            // FAK: 部分成交后取消剩余，最终状态为 Cancelled
            // 注：如果有部分成交，成交记录保留但订单状态为 Cancelled
            return ExecutionStatus.Cancelled;
        }

        return currentStatus;
    }
}
