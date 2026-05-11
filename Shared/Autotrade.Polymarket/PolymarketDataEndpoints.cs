namespace Autotrade.Polymarket;

/// <summary>
/// Polymarket Data API 端点（相对路径）。
/// Data API 用于查询用户持仓等信息（公开 API，无需签名）。
/// </summary>
public static class PolymarketDataEndpoints
{
    /// <summary>
    /// 获取用户当前持仓。
    /// Query params: user, market, redeemable, mergeable, limit, offset
    /// </summary>
    public const string GetPositions = "/positions";

    /// <summary>
    /// 获取用户已平仓持仓记录。
    /// </summary>
    public const string GetClosedPositions = "/closed-positions";
}
