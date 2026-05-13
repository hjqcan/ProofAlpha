using Autotrade.Polymarket.Models;

namespace Autotrade.Polymarket.Abstractions;

/// <summary>
/// Polymarket CLOB REST API 客户端抽象（便于 mock 与测试）。
/// </summary>
public interface IPolymarketClobClient
{
    // ─────────────────────────────────────────────────────────────
    // Server
    // ─────────────────────────────────────────────────────────────

    Task<PolymarketApiResult<long>> GetServerTimeAsync(CancellationToken cancellationToken = default);

    // ─────────────────────────────────────────────────────────────
    // Auth / API Keys
    // ─────────────────────────────────────────────────────────────

    Task<PolymarketApiResult<ApiKeyCreds>> CreateApiKeyAsync(int? nonce = null, CancellationToken cancellationToken = default);

    Task<PolymarketApiResult<ApiKeyCreds>> DeriveApiKeyAsync(int? nonce = null, CancellationToken cancellationToken = default);

    Task<PolymarketApiResult<ApiKeysResponse>> GetApiKeysAsync(CancellationToken cancellationToken = default);

    Task<PolymarketApiResult<BanStatus>> GetClosedOnlyModeAsync(CancellationToken cancellationToken = default);

    Task<PolymarketApiResult<string>> DeleteApiKeyAsync(CancellationToken cancellationToken = default);

    // ─────────────────────────────────────────────────────────────
    // Markets
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 获取所有市场（分页）。
    /// </summary>
    Task<PolymarketApiResult<IReadOnlyList<MarketInfo>>> GetMarketsAsync(string? nextCursor = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定市场。
    /// </summary>
    Task<PolymarketApiResult<MarketInfo>> GetMarketAsync(string conditionId, CancellationToken cancellationToken = default);

    // ─────────────────────────────────────────────────────────────
    // Order Book / Pricing
    // ─────────────────────────────────────────────────────────────

    Task<PolymarketApiResult<OrderBookSummary>> GetOrderBookAsync(string tokenId, CancellationToken cancellationToken = default);

    // ─────────────────────────────────────────────────────────────
    // Orders
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 下单（需 L2 认证 + 签名）。
    /// </summary>
    Task<PolymarketApiResult<OrderResponse>> PlaceOrderAsync(OrderRequest request, string? idempotencyKey = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 下单（需 L2 认证 + 已签名订单 envelope）。
    /// </summary>
    Task<PolymarketApiResult<OrderResponse>> PlaceOrderAsync(PostOrderRequest request, string? idempotencyKey = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量下单（需 L2 认证 + 已签名订单 envelope）。
    /// </summary>
    Task<PolymarketApiResult<IReadOnlyList<OrderResponse>>> PlaceOrdersAsync(
        IReadOnlyList<PostOrderRequest> requests,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 取消单个订单。
    /// </summary>
    Task<PolymarketApiResult<CancelOrderResponse>> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取消所有订单（可选按市场或资产过滤）。
    /// </summary>
    Task<PolymarketApiResult<CancelOrderResponse>> CancelAllOrdersAsync(string? market = null, string? assetId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定订单详情。
    /// </summary>
    Task<PolymarketApiResult<OrderInfo>> GetOrderAsync(string orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取当前所有开放订单。
    /// </summary>
    Task<PolymarketApiResult<IReadOnlyList<OrderInfo>>> GetOpenOrdersAsync(string? market = null, string? assetId = null, CancellationToken cancellationToken = default);

    // ─────────────────────────────────────────────────────────────
    // Trades
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 获取用户交易记录。
    /// </summary>
    Task<PolymarketApiResult<IReadOnlyList<TradeInfo>>> GetTradesAsync(string? market = null, string? nextCursor = null, CancellationToken cancellationToken = default);

    // ─────────────────────────────────────────────────────────────
    // Balance
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Get trades attributed to a Polymarket builder code.
    /// </summary>
    Task<PolymarketApiResult<IReadOnlyList<BuilderTradeInfo>>> GetBuilderTradesAsync(
        string builderCode,
        string? market = null,
        string? assetId = null,
        string? tradeId = null,
        string? before = null,
        string? after = null,
        string? nextCursor = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取账户余额与授权信息（USDC collateral）。
    /// </summary>
    Task<PolymarketApiResult<BalanceAllowance>> GetBalanceAllowanceAsync(CancellationToken cancellationToken = default);
}
