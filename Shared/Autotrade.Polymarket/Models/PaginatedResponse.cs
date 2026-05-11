using System.Text.Json.Serialization;

namespace Autotrade.Polymarket.Models;

/// <summary>
/// 通用分页响应：data + next_cursor。
/// </summary>
public sealed class PaginatedResponse<T>
{
    [JsonPropertyName("data")]
    public List<T> Data { get; init; } = [];

    [JsonPropertyName("next_cursor")]
    public string NextCursor { get; init; } = PolymarketConstants.EndCursor;

    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    [JsonPropertyName("count")]
    public int? Count { get; init; }
}

