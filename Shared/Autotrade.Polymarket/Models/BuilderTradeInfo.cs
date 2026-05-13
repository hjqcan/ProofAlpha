using System.Text.Json.Serialization;

namespace Autotrade.Polymarket.Models;

/// <summary>
/// Trade attributed to a Polymarket builder code.
/// </summary>
public sealed record BuilderTradeInfo
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("tradeType")]
    public string? TradeType { get; init; }

    [JsonPropertyName("takerOrderHash")]
    public string? TakerOrderHash { get; init; }

    [JsonPropertyName("makerOrderHash")]
    public string? MakerOrderHash { get; init; }

    [JsonPropertyName("market")]
    public string Market { get; init; } = string.Empty;

    [JsonPropertyName("asset")]
    public string? Asset { get; init; }

    [JsonPropertyName("assetId")]
    public string? AssetId { get; init; }

    [JsonPropertyName("side")]
    public string Side { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public string Size { get; init; } = "0";

    [JsonPropertyName("sizeUsdc")]
    public string? SizeUsdc { get; init; }

    [JsonPropertyName("price")]
    public string Price { get; init; } = "0";

    [JsonPropertyName("fee")]
    public string? Fee { get; init; }

    [JsonPropertyName("feeUsdc")]
    public string? FeeUsdc { get; init; }

    [JsonPropertyName("feeRateBps")]
    public string? FeeRateBps { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("matchTime")]
    public string? MatchTime { get; init; }

    [JsonPropertyName("lastUpdate")]
    public string? LastUpdate { get; init; }

    [JsonPropertyName("outcome")]
    public string? Outcome { get; init; }

    [JsonPropertyName("outcomeIndex")]
    public int? OutcomeIndex { get; init; }

    [JsonPropertyName("owner")]
    public string? Owner { get; init; }

    [JsonPropertyName("maker")]
    public string? Maker { get; init; }

    [JsonPropertyName("builder")]
    public string Builder { get; init; } = string.Empty;

    [JsonPropertyName("transactionHash")]
    public string? TransactionHash { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; init; }
}
