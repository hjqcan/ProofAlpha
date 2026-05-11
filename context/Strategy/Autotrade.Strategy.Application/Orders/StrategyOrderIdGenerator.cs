// ============================================================================
// 策略订单 ID 生成器
// ============================================================================
// 生成唯一的客户端订单 ID。
// 格式：strat-{strategyId}-{marketId}-{suffix}-{shortGuid}
// ============================================================================

namespace Autotrade.Strategy.Application.Orders;

/// <summary>
/// 策略订单 ID 生成器。
/// </summary>
public static class StrategyOrderIdGenerator
{
    /// <summary>
    /// 创建唯一的客户端订单 ID。
    /// </summary>
    /// <param name="strategyId">策略 ID。</param>
    /// <param name="marketId">市场 ID。</param>
    /// <param name="suffix">后缀（如 First/Second）。</param>
    /// <returns>唯一的订单 ID。</returns>
    public static string Create(string strategyId, string marketId, string suffix)
    {
        if (string.IsNullOrWhiteSpace(strategyId))
        {
            throw new ArgumentException("StrategyId cannot be empty.", nameof(strategyId));
        }

        if (string.IsNullOrWhiteSpace(marketId))
        {
            throw new ArgumentException("MarketId cannot be empty.", nameof(marketId));
        }

        var shortId = Guid.NewGuid().ToString("N")[..8];
        return $"strat-{strategyId}-{marketId}-{suffix}-{shortId}";
    }
}
