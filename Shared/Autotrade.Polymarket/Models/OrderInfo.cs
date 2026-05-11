using System.Text.Json.Serialization;

namespace Autotrade.Polymarket.Models;

/// <summary>
/// Polymarket 订单信息。
/// </summary>
public sealed record OrderInfo
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("owner")]
    public string Owner { get; init; } = string.Empty;

    [JsonPropertyName("market")]
    public string Market { get; init; } = string.Empty;

    [JsonPropertyName("asset_id")]
    public string AssetId { get; init; } = string.Empty;

    [JsonPropertyName("side")]
    public string Side { get; init; } = string.Empty;

    [JsonPropertyName("original_size")]
    public string OriginalSize { get; init; } = "0";

    [JsonPropertyName("size_matched")]
    public string SizeMatched { get; init; } = "0";

    [JsonPropertyName("price")]
    public string Price { get; init; } = "0";

    [JsonPropertyName("outcome")]
    public string? Outcome { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("expiration")]
    public string? Expiration { get; init; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; init; }

    [JsonPropertyName("associate_trades")]
    public IReadOnlyList<string>? AssociateTrades { get; init; }
}
