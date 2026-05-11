using System.Text.Json.Serialization;

namespace Autotrade.Polymarket.Models;

/// <summary>
/// 账户余额和授权信息。
/// </summary>
public sealed record BalanceAllowance
{
    [JsonPropertyName("balance")]
    public string Balance { get; init; } = "0";

    [JsonPropertyName("allowance")]
    public string Allowance { get; init; } = "0";
}

/// <summary>
/// 多Token余额/授权响应（批量查询）。
/// </summary>
public sealed record MultiBalanceAllowance
{
    /// <summary>
    /// 键为 token_id 或 asset_id，值为余额授权信息。
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, object?>? Assets { get; init; }
}
