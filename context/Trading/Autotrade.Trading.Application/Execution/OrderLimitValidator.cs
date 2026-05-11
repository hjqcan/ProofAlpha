using Autotrade.Trading.Application.Contract.Execution;
using Microsoft.Extensions.Options;

namespace Autotrade.Trading.Application.Execution;

/// <summary>
/// 订单限制验证器：检查配置限制（最大挂单数等）。
/// </summary>
public sealed class OrderLimitValidator
{
    private readonly IOrderStateTracker _stateTracker;
    private readonly ExecutionOptions _options;

    public OrderLimitValidator(
        IOrderStateTracker stateTracker,
        IOptions<ExecutionOptions> options)
    {
        _stateTracker = stateTracker ?? throw new ArgumentNullException(nameof(stateTracker));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// 验证是否可以在指定市场下新订单。
    /// </summary>
    /// <param name="marketId">市场 ID。</param>
    /// <returns>验证结果，通过返回 null，否则返回错误消息。</returns>
    public string? ValidateCanPlaceOrder(string marketId)
    {
        if (string.IsNullOrWhiteSpace(marketId))
        {
            return null; // 无法验证，允许通过
        }

        var currentCount = _stateTracker.GetOpenOrderCount(marketId);

        if (currentCount >= _options.MaxOpenOrdersPerMarket)
        {
            return $"市场 {marketId} 已达到最大挂单数限制 ({currentCount}/{_options.MaxOpenOrdersPerMarket})";
        }

        return null;
    }

    /// <summary>
    /// 获取指定市场的剩余可下单数量。
    /// </summary>
    /// <param name="marketId">市场 ID。</param>
    /// <returns>剩余可下单数量。</returns>
    public int GetRemainingOrderSlots(string marketId)
    {
        if (string.IsNullOrWhiteSpace(marketId))
        {
            return _options.MaxOpenOrdersPerMarket;
        }

        var currentCount = _stateTracker.GetOpenOrderCount(marketId);
        return Math.Max(0, _options.MaxOpenOrdersPerMarket - currentCount);
    }

    /// <summary>
    /// 获取所有市场的挂单统计。
    /// </summary>
    /// <returns>市场 ID 到 (当前挂单数, 最大挂单数) 的映射。</returns>
    public IReadOnlyDictionary<string, (int Current, int Max)> GetOrderLimitStats()
    {
        var counts = _stateTracker.GetAllOpenOrderCounts();
        var result = new Dictionary<string, (int Current, int Max)>();

        foreach (var (marketId, count) in counts)
        {
            result[marketId] = (count, _options.MaxOpenOrdersPerMarket);
        }

        return result;
    }
}
