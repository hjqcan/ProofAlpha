using System.Text.Json.Serialization;

namespace Autotrade.Polymarket.Models;

/// <summary>
/// Gamma /markets 返回的 Market（仅保留本项目需要的字段）。
/// 文档参考：third-party/polymarket-websocket-client/docs/api-reference/markets/list-markets.md
/// </summary>
public sealed record GammaMarket
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("conditionId")]
    public string ConditionId { get; init; } = string.Empty;

    [JsonPropertyName("question")]
    public string? Question { get; init; }

    [JsonPropertyName("slug")]
    public string? Slug { get; init; }

    [JsonPropertyName("category")]
    public string? Category { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("active")]
    public bool? Active { get; init; }

    [JsonPropertyName("closed")]
    public bool? Closed { get; init; }

    [JsonPropertyName("volumeNum")]
    public decimal? VolumeNum { get; init; }

    [JsonPropertyName("liquidityNum")]
    public decimal? LiquidityNum { get; init; }

    [JsonPropertyName("volume24hr")]
    public decimal? Volume24hr { get; init; }

    [JsonPropertyName("volume24hrClob")]
    public decimal? Volume24hrClob { get; init; }

    [JsonPropertyName("outcomes")]
    public string? Outcomes { get; init; }

    [JsonPropertyName("outcomePrices")]
    public string? OutcomePrices { get; init; }

    /// <summary>
    /// ISO8601 string (nullable).
    /// </summary>
    [JsonPropertyName("endDateIso")]
    public string? EndDateIso { get; init; }

    /// <summary>
    /// JSON array string of token ids (nullable), e.g. ["123","456"].
    /// </summary>
    [JsonPropertyName("clobTokenIds")]
    public string? ClobTokenIds { get; init; }

    [JsonPropertyName("acceptingOrders")]
    public bool? AcceptingOrders { get; init; }

    [JsonPropertyName("bestBid")]
    public decimal? BestBid { get; init; }

    [JsonPropertyName("bestAsk")]
    public decimal? BestAsk { get; init; }

    [JsonPropertyName("spread")]
    public decimal? Spread { get; init; }
}

