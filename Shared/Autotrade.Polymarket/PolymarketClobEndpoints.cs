namespace Autotrade.Polymarket;

/// <summary>
/// Polymarket CLOB REST API 端点（相对路径）。
/// 注意：签名时使用的 requestPath 通常不包含 querystring（与官方客户端一致）。
/// </summary>
public static class PolymarketClobEndpoints
{
    // Server Time
    public const string Time = "/time";

    // Auth / API keys
    public const string CreateApiKey = "/auth/api-key";
    public const string GetApiKeys = "/auth/api-keys";
    public const string DeleteApiKey = "/auth/api-key";
    public const string DeriveApiKey = "/auth/derive-api-key";
    public const string ClosedOnly = "/auth/ban-status/closed-only";

    // Readonly API key
    public const string CreateReadonlyApiKey = "/auth/readonly-api-key";
    public const string GetReadonlyApiKeys = "/auth/readonly-api-keys";
    public const string DeleteReadonlyApiKey = "/auth/readonly-api-key";
    public const string ValidateReadonlyApiKey = "/auth/validate-readonly-api-key";

    // Markets
    public const string GetSamplingSimplifiedMarkets = "/sampling-simplified-markets";
    public const string GetSamplingMarkets = "/sampling-markets";
    public const string GetSimplifiedMarkets = "/simplified-markets";
    public const string GetMarkets = "/markets";
    public const string GetMarketPrefix = "/markets/";

    // Order book / pricing
    public const string GetOrderBook = "/book";
    public const string GetOrderBooks = "/books";
    public const string GetMidpoint = "/midpoint";
    public const string GetMidpoints = "/midpoints";
    public const string GetPrice = "/price";
    public const string GetPrices = "/prices";
    public const string GetSpread = "/spread";
    public const string GetSpreads = "/spreads";
    public const string GetLastTradePrice = "/last-trade-price";
    public const string GetLastTradesPrices = "/last-trades-prices";
    public const string GetTickSize = "/tick-size";
    public const string GetNegRisk = "/neg-risk";
    public const string GetFeeRate = "/fee-rate";

    // Orders / Trades
    public const string PostOrder = "/order";
    public const string PostOrders = "/orders";
    public const string CancelOrder = "/order";
    public const string CancelOrders = "/orders";
    public const string GetOrderPrefix = "/data/order/";
    public const string CancelAll = "/cancel-all";
    public const string CancelMarketOrders = "/cancel-market-orders";
    public const string GetOpenOrders = "/data/orders";
    public const string GetTrades = "/data/trades";

    // Balance & allowance
    public const string GetBalanceAllowance = "/balance-allowance";
    public const string UpdateBalanceAllowance = "/balance-allowance/update";

    // Notifications
    public const string GetNotifications = "/notifications";
    public const string DropNotifications = "/notifications";

    // Live activity
    public const string GetMarketTradesEventsPrefix = "/live-activity/events/";
}

