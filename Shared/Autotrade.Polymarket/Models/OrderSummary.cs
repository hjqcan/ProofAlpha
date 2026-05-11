using System.Text.Json.Serialization;

namespace Autotrade.Polymarket.Models;

/// <summary>
/// 订单簿档位（price/size）。
/// 注意：服务端可能返回字符串或数字，这里用 string 保守接收，上层可自行解析为 decimal。
/// </summary>
public sealed class OrderSummary
{
    [JsonPropertyName("price")]
    public string Price { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public string Size { get; init; } = string.Empty;
}

