using System.Text.Json.Serialization;

namespace Autotrade.Polymarket.Models;

/// <summary>
/// CLOB 订单簿摘要（/book）。
/// </summary>
public sealed class OrderBookSummary
{
    [JsonPropertyName("market")]
    public string? Market { get; init; }

    [JsonPropertyName("asset_id")]
    public string? AssetId { get; init; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; init; }

    [JsonPropertyName("min_order_size")]
    public string? MinOrderSize { get; init; }

    [JsonPropertyName("neg_risk")]
    public bool NegRisk { get; init; }

    [JsonPropertyName("tick_size")]
    public string? TickSize { get; init; }

    [JsonPropertyName("bids")]
    public List<OrderSummary> Bids { get; init; } = [];

    [JsonPropertyName("asks")]
    public List<OrderSummary> Asks { get; init; } = [];

    [JsonPropertyName("hash")]
    public string? Hash { get; init; }
}

