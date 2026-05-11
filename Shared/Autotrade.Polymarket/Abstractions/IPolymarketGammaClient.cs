using Autotrade.Polymarket.Models;

namespace Autotrade.Polymarket.Abstractions;

/// <summary>
/// Polymarket Gamma（市场元数据）API 客户端（只读）。
/// </summary>
public interface IPolymarketGammaClient
{
    /// <summary>
    /// 获取 Markets 列表（Gamma /markets，offset 分页）。
    /// </summary>
    Task<PolymarketApiResult<IReadOnlyList<GammaMarket>>> ListMarketsAsync(
        int limit = 100,
        int offset = 0,
        bool closed = false,
        string? order = "id",
        bool ascending = false,
        CancellationToken cancellationToken = default);
}

