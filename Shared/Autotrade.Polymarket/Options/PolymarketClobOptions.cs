namespace Autotrade.Polymarket.Options;

/// <summary>
/// Polymarket CLOB 客户端配置项（可通过 appsettings.json + 环境变量覆盖）。
/// </summary>
public sealed class PolymarketClobOptions
{
    public const string SectionName = "Polymarket:Clob";

    /// <summary>
    /// CLOB API Host，例如： https://clob.polymarket.com
    /// </summary>
    public string Host { get; set; } = "https://clob.polymarket.com";

    /// <summary>
    /// 链 ID（Polygon 主网=137）。
    /// </summary>
    public int ChainId { get; set; } = 137;

    /// <summary>
    /// 钱包私钥（敏感）：用于 L1 EIP-712 签名，以及获取地址（L2 Header 也需要地址）。
    /// 建议通过 User Secrets 或环境变量注入，不要写入仓库。
    /// </summary>
    public string? PrivateKey { get; set; }

    /// <summary>
    /// 可选：如果你不想在服务进程内保留私钥，可只提供地址用于 L2 调用（前提：不需要 L1 能力）。
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Optional funder/proxy wallet address used as the order maker. Defaults to the signing address.
    /// </summary>
    public string? Funder { get; set; }

    /// <summary>
    /// CLOB order signing protocol version. Only V2 is supported after the 2026-04-28 cutover.
    /// </summary>
    public int OrderSigningVersion { get; set; } = 2;

    /// <summary>
    /// EIP-712 order signature type. 0=EOA, 1=Polymarket proxy, 2=Gnosis safe.
    /// </summary>
    public int SignatureType { get; set; } = 0;

    /// <summary>
    /// Optional override for the CTF exchange contract used in standard markets.
    /// </summary>
    public string? ExchangeAddress { get; set; }

    /// <summary>
    /// Optional override for the CTF exchange contract used in neg-risk markets.
    /// </summary>
    public string? NegRiskExchangeAddress { get; set; }

    /// <summary>
    /// Optional V2 builder bytes32 metadata. Defaults to 0x00..00.
    /// </summary>
    public string? BuilderCode { get; set; }

    /// <summary>
    /// Optional V2 bytes32 metadata. Defaults to 0x00..00.
    /// </summary>
    public string? OrderMetadata { get; set; }

    /// <summary>
    /// L2 API Key（非明文敏感但建议脱敏日志输出）。
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// L2 API Secret（敏感，base64 格式）。
    /// </summary>
    public string? ApiSecret { get; set; }

    /// <summary>
    /// L2 API Passphrase（敏感）。
    /// </summary>
    public string? ApiPassphrase { get; set; }

    /// <summary>
    /// 是否使用 /time 的服务器时间生成签名时间戳（更稳但会多一次网络调用）。
    /// </summary>
    public bool UseServerTime { get; set; } = false;

    /// <summary>
    /// HttpClient 超时（建议略大于下游 P99）。
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 是否禁用系统代理（测试场景常用）。
    /// </summary>
    public bool DisableProxy { get; set; } = false;
}
