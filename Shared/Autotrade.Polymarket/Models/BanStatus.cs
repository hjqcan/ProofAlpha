using System.Text.Json.Serialization;

namespace Autotrade.Polymarket.Models;

/// <summary>
/// 账号状态：是否处于 closed-only（只能平仓不能开仓）。
/// </summary>
public sealed class BanStatus
{
    [JsonPropertyName("closedOnly")]
    public bool ClosedOnly { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

