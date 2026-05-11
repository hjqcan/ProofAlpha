using System.Text.Json.Serialization;
using System.Globalization;

namespace Autotrade.Polymarket.Models;

/// <summary>
/// 用户持仓信息（来自 Data API /positions 端点）。
/// </summary>
public sealed record UserPosition
{
    /// <summary>
    /// 资产 Token ID。
    /// </summary>
    [JsonPropertyName("asset")]
    public string Asset { get; init; } = string.Empty;

    /// <summary>
    /// 市场 Condition ID。
    /// </summary>
    [JsonPropertyName("conditionId")]
    public string ConditionId { get; init; } = string.Empty;

    /// <summary>
    /// 结果方向（"Yes" / "No"）。
    /// </summary>
    [JsonPropertyName("outcome")]
    public string Outcome { get; init; } = string.Empty;

    /// <summary>
    /// 持仓数量（原始字符串，避免精度问题）。
    /// </summary>
    [JsonPropertyName("size")]
    public string Size { get; init; } = "0";

    /// <summary>
    /// 平均入场价格（原始字符串）。
    /// </summary>
    [JsonPropertyName("avgPrice")]
    public string AvgPrice { get; init; } = "0";

    /// <summary>
    /// 当前市场价格（原始字符串）。
    /// </summary>
    [JsonPropertyName("curPrice")]
    public string CurPrice { get; init; } = "0";

    /// <summary>
    /// 持仓价值（原始字符串）。
    /// </summary>
    [JsonPropertyName("value")]
    public string Value { get; init; } = "0";

    /// <summary>
    /// 未实现盈亏（原始字符串）。
    /// </summary>
    [JsonPropertyName("pnl")]
    public string Pnl { get; init; } = "0";

    /// <summary>
    /// 是否可赎回。
    /// </summary>
    [JsonPropertyName("redeemable")]
    public bool Redeemable { get; init; }

    /// <summary>
    /// 是否可合并。
    /// </summary>
    [JsonPropertyName("mergeable")]
    public bool Mergeable { get; init; }

    /// <summary>
    /// 市场标题（可选）。
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>
    /// 解析后的持仓数量。
    /// </summary>
    public decimal SizeDecimal => TryParseDecimal(Size, out var v) ? v : 0m;

    /// <summary>
    /// 解析后的平均入场价格。
    /// </summary>
    public decimal AvgPriceDecimal => TryParseDecimal(AvgPrice, out var v) ? v : 0m;

    /// <summary>
    /// 解析后的当前价格。
    /// </summary>
    public decimal CurPriceDecimal => TryParseDecimal(CurPrice, out var v) ? v : 0m;

    private static bool TryParseDecimal(string? raw, out decimal value)
        => decimal.TryParse(
            raw,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);
}

/// <summary>
/// 持仓查询分页响应。
/// </summary>
public sealed record UserPositionsResponse
{
    /// <summary>
    /// 持仓列表。
    /// </summary>
    [JsonPropertyName("data")]
    public IReadOnlyList<UserPosition> Data { get; init; } = Array.Empty<UserPosition>();

    /// <summary>
    /// 下一页游标（若有）。
    /// </summary>
    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; init; }

    /// <summary>
    /// 总条数（若 API 返回）。
    /// </summary>
    [JsonPropertyName("count")]
    public int? Count { get; init; }
}
