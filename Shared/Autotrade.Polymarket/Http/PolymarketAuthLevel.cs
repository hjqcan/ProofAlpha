namespace Autotrade.Polymarket.Http;

/// <summary>
/// Polymarket CLOB 鉴权等级：
/// - None：公开接口
/// - L1：EIP-712（创建/派生 API Key、以及后续订单签名）
/// - L2：HMAC（带 API Key 的私有接口）
/// </summary>
public enum PolymarketAuthLevel
{
    None = 0,
    L1 = 1,
    L2 = 2
}

