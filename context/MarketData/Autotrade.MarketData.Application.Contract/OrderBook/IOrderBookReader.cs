using Autotrade.Application.Services;
using Autotrade.MarketData.Domain.Shared.ValueObjects;

namespace Autotrade.MarketData.Application.Contract.OrderBook;

/// <summary>
/// 订单簿只读查询接口（用于跨上下文依赖）。
/// </summary>
public interface IOrderBookReader : IApplicationService
{
    /// <summary>
    /// 获取最优买卖价。
    /// </summary>
    /// <param name="assetId">资产 ID。</param>
    /// <returns>最优买卖价信息。</returns>
    TopOfBookDto? GetTopOfBook(string assetId);

    /// <summary>
    /// 获取深度档位。
    /// </summary>
    /// <param name="assetId">资产 ID。</param>
    /// <param name="levels">档位数量。</param>
    /// <returns>价格档位列表。</returns>
    IReadOnlyList<PriceLevelDto> GetDepth(string assetId, int levels = 10);
}

/// <summary>
/// 最优买卖价 DTO。
/// </summary>
public sealed record TopOfBookDto(
    string AssetId,
    Price? BestBidPrice,
    Quantity? BestBidSize,
    Price? BestAskPrice,
    Quantity? BestAskSize,
    decimal? Spread,
    DateTimeOffset LastUpdatedUtc);

/// <summary>
/// 价格档位 DTO。
/// </summary>
public sealed record PriceLevelDto(decimal Price, decimal Size, bool IsBid);
