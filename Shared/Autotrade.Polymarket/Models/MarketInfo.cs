using System.Text.Json.Serialization;

namespace Autotrade.Polymarket.Models;

/// <summary>
/// Polymarket 市场基本信息。
/// </summary>
public sealed record MarketInfo
{
    [JsonPropertyName("condition_id")]
    public string ConditionId { get; init; } = string.Empty;

    [JsonPropertyName("question_id")]
    public string? QuestionId { get; init; }

    [JsonPropertyName("tokens")]
    public IReadOnlyList<TokenInfo>? Tokens { get; init; }

    [JsonPropertyName("min_incentive_size")]
    public string? MinIncentiveSize { get; init; }

    [JsonPropertyName("max_incentive_spread")]
    public string? MaxIncentiveSpread { get; init; }

    [JsonPropertyName("active")]
    public bool Active { get; init; }

    [JsonPropertyName("closed")]
    public bool Closed { get; init; }

    [JsonPropertyName("accepting_orders")]
    public bool AcceptingOrders { get; init; }

    [JsonPropertyName("accepting_order_timestamp")]
    public string? AcceptingOrderTimestamp { get; init; }

    [JsonPropertyName("minimum_order_size")]
    public string? MinimumOrderSize { get; init; }

    [JsonPropertyName("minimum_tick_size")]
    public string? MinimumTickSize { get; init; }

    [JsonPropertyName("end_date_iso")]
    public string? EndDateIso { get; init; }

    [JsonPropertyName("game_start_time")]
    public string? GameStartTime { get; init; }

    [JsonPropertyName("seconds_delay")]
    public int? SecondsDelay { get; init; }

    [JsonPropertyName("fpmm")]
    public string? Fpmm { get; init; }

    [JsonPropertyName("maker_base_fee")]
    public int? MakerBaseFee { get; init; }

    [JsonPropertyName("taker_base_fee")]
    public int? TakerBaseFee { get; init; }

    [JsonPropertyName("neg_risk")]
    public bool NegRisk { get; init; }

    [JsonPropertyName("icon")]
    public string? Icon { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("tags")]
    public IReadOnlyList<string>? Tags { get; init; }

    [JsonPropertyName("rewards")]
    public RewardsConfig? Rewards { get; init; }
}

/// <summary>
/// 市场中的 Token 信息。
/// </summary>
public sealed record TokenInfo
{
    [JsonPropertyName("token_id")]
    public string TokenId { get; init; } = string.Empty;

    [JsonPropertyName("outcome")]
    public string Outcome { get; init; } = string.Empty;

    [JsonPropertyName("price")]
    public string? Price { get; init; }

    [JsonPropertyName("winner")]
    public bool? Winner { get; init; }
}

/// <summary>
/// 市场奖励配置。
/// </summary>
public sealed record RewardsConfig
{
    [JsonPropertyName("rates")]
    public IReadOnlyList<RewardRate>? Rates { get; init; }

    [JsonPropertyName("min_size")]
    public string? MinSize { get; init; }

    [JsonPropertyName("max_spread")]
    public string? MaxSpread { get; init; }

    [JsonPropertyName("event_start_date")]
    public string? EventStartDate { get; init; }

    [JsonPropertyName("event_end_date")]
    public string? EventEndDate { get; init; }

    [JsonPropertyName("in_game_multiplier")]
    public string? InGameMultiplier { get; init; }
}

/// <summary>
/// 奖励费率。
/// </summary>
public sealed record RewardRate
{
    [JsonPropertyName("asset_address")]
    public string? AssetAddress { get; init; }

    [JsonPropertyName("rewards_daily_rate")]
    public string? RewardsDailyRate { get; init; }
}
