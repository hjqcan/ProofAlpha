namespace Autotrade.Polymarket;

/// <summary>
/// Polymarket CLOB 协议常量。
/// 参考官方开源客户端（TypeScript: polymarket/clob-client，Python: polymarket/py-clob-client）。
/// </summary>
public static class PolymarketConstants
{
    // ========== Headers ==========
    public const string PolyAddressHeader = "POLY_ADDRESS";
    public const string PolySignatureHeader = "POLY_SIGNATURE";
    public const string PolyTimestampHeader = "POLY_TIMESTAMP";
    public const string PolyNonceHeader = "POLY_NONCE";
    public const string PolyApiKeyHeader = "POLY_API_KEY";
    public const string PolyPassphraseHeader = "POLY_PASSPHRASE";
    public const string POLY_IDEMPOTENCY_KEY_HEADER = "POLY_IDEMPOTENCY_KEY";

    // ========== EIP-712 (L1) ==========
    public const string ClobDomainName = "ClobAuthDomain";
    public const string ClobDomainVersion = "1";
    public const string ClobAuthMessageToSign = "This message attests that I control the given wallet";

    // ========== Pagination ==========
    public const string InitialCursor = "MA==";
    public const string EndCursor = "LTE=";
}

