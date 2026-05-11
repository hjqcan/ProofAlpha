using System.Text.Json.Serialization;

namespace Autotrade.Polymarket.Models;

/// <summary>
/// Polymarket 交易（成交）信息。
/// </summary>
public sealed record TradeInfo
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("taker_order_id")]
    public string? TakerOrderId { get; init; }

    [JsonPropertyName("market")]
    public string Market { get; init; } = string.Empty;

    [JsonPropertyName("asset_id")]
    public string AssetId { get; init; } = string.Empty;

    [JsonPropertyName("side")]
    public string Side { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public string Size { get; init; } = "0";

    [JsonPropertyName("fee_rate_bps")]
    public string? FeeRateBps { get; init; }

    [JsonPropertyName("price")]
    public string Price { get; init; } = "0";

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("match_time")]
    public string? MatchTime { get; init; }

    [JsonPropertyName("last_update")]
    public string? LastUpdate { get; init; }

    [JsonPropertyName("outcome")]
    public string? Outcome { get; init; }

    [JsonPropertyName("owner")]
    public string? Owner { get; init; }

    [JsonPropertyName("maker_address")]
    public string? MakerAddress { get; init; }

    [JsonPropertyName("transaction_hash")]
    public string? TransactionHash { get; init; }

    [JsonPropertyName("bucket_index")]
    public int? BucketIndex { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }
}
