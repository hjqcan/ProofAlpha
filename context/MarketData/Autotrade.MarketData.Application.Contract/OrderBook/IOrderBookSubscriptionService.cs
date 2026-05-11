using Autotrade.Application.Services;

namespace Autotrade.MarketData.Application.Contract.OrderBook;

/// <summary>
/// 订单簿订阅管理接口（用于跨上下文）。
/// 由 MarketData 上下文实现，Strategy 上下文调用以确保所需市场的 TokenId 已订阅。
/// </summary>
public interface IOrderBookSubscriptionService : IApplicationService
{
    /// <summary>
    /// 确保给定市场集合对应的 TokenId 已被订阅（并取消不再需要的订阅）。
    /// </summary>
    Task EnsureSubscribedMarketsAsync(
        IReadOnlyCollection<string> marketIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 当前已订阅的资产（TokenId）数量（用于诊断/状态输出）。
    /// </summary>
    int SubscribedAssetCount { get; }
}

