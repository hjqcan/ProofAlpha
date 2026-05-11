namespace Autotrade.Polymarket.Http;

/// <summary>
/// 客户端侧限流拒绝异常（区别于服务端 429）。
/// </summary>
public sealed class PolymarketRateLimitRejectedException : Exception
{
    public PolymarketRateLimitRejectedException(string message) : base(message)
    {
    }
}

