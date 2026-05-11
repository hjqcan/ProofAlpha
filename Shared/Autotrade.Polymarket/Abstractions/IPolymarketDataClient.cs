using Autotrade.Polymarket.Models;

namespace Autotrade.Polymarket.Abstractions;

/// <summary>
/// Polymarket Data API 客户端抽象（用于查询持仓等用户数据）。
/// Data API 是公开 API，无需签名认证。
/// </summary>
public interface IPolymarketDataClient
{
    /// <summary>
    /// 获取用户当前持仓列表。
    /// </summary>
    /// <param name="userAddress">用户钱包地址（必填）。</param>
    /// <param name="market">市场 Condition ID 过滤（可选）。</param>
    /// <param name="redeemable">仅返回可赎回持仓（可选）。</param>
    /// <param name="mergeable">仅返回可合并持仓（可选）。</param>
    /// <param name="limit">分页大小（默认 100，最大 500）。</param>
    /// <param name="offset">分页偏移。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>持仓列表结果。</returns>
    Task<PolymarketApiResult<IReadOnlyList<UserPosition>>> GetPositionsAsync(
        string userAddress,
        string? market = null,
        bool? redeemable = null,
        bool? mergeable = null,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户已平仓持仓记录。
    /// </summary>
    /// <param name="userAddress">用户钱包地址（必填）。</param>
    /// <param name="limit">分页大小。</param>
    /// <param name="offset">分页偏移。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已平仓持仓列表结果。</returns>
    Task<PolymarketApiResult<IReadOnlyList<UserPosition>>> GetClosedPositionsAsync(
        string userAddress,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default);
}
