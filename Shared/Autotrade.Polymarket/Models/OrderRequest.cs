using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Autotrade.Polymarket.Models;

/// <summary>
/// 下单请求参数。
/// </summary>
public sealed record OrderRequest
{
    /// <summary>
    /// Token ID（资产）。
    /// </summary>
    [JsonPropertyName("tokenID")]
    public required string TokenId { get; init; }

    /// <summary>
    /// 价格（0.01~0.99）。
    /// </summary>
    [JsonPropertyName("price")]
    public required string Price { get; init; }

    /// <summary>
    /// 数量。
    /// </summary>
    [JsonPropertyName("size")]
    public required string Size { get; init; }

    /// <summary>
    /// 买/卖方向：BUY 或 SELL。
    /// </summary>
    [JsonPropertyName("side")]
    public required string Side { get; init; }

    /// <summary>
    /// 费用率（BPS）。
    /// </summary>
    [JsonPropertyName("feeRateBps")]
    public string? FeeRateBps { get; init; }

    /// <summary>
    /// 订单有效期/类型：FAK / FOK / GTC / GTD。
    /// </summary>
    [JsonPropertyName("tif")]
    public string? TimeInForce { get; init; }

    /// <summary>
    /// GTD 时的到期时间戳（秒）。
    /// </summary>
    [JsonPropertyName("expiration")]
    public long? Expiration { get; init; }

    /// <summary>
    /// 是否为 neg_risk 市场。
    /// </summary>
    [JsonPropertyName("negRisk")]
    public bool? NegRisk { get; init; }

    [JsonIgnore]
    public string? Taker { get; init; }

    [JsonIgnore]
    public string? Nonce { get; init; }

    [JsonIgnore]
    public string? Salt { get; init; }

    [JsonIgnore]
    public string? Timestamp { get; init; }

    [JsonIgnore]
    public string? Metadata { get; init; }

    [JsonIgnore]
    public string? Builder { get; init; }
}

/// <summary>
/// 下单响应。
/// </summary>
public sealed record OrderResponse
{
    [JsonPropertyName("orderID")]
    public string? OrderId { get; init; }

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("errorMsg")]
    public string? ErrorMsg { get; init; }

    [JsonPropertyName("transactionsHashes")]
    public IReadOnlyList<string>? TransactionHashes { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

/// <summary>
/// Signed order envelope accepted by POST /orders.
/// </summary>
public sealed record PostOrderRequest
{
    [JsonPropertyName("order")]
    public required SignedClobOrder Order { get; init; }

    [JsonPropertyName("owner")]
    public required string Owner { get; init; }

    [JsonPropertyName("orderType")]
    public required string OrderType { get; init; }

    [JsonPropertyName("deferExec")]
    public bool? DeferExecution { get; init; }
}

/// <summary>
/// CLOB EIP-712 signed order payload.
/// </summary>
public sealed record SignedClobOrder
{
    [JsonPropertyName("salt")]
    [JsonConverter(typeof(DecimalStringJsonNumberConverter))]
    public required string Salt { get; init; }

    [JsonPropertyName("maker")]
    public required string Maker { get; init; }

    [JsonPropertyName("signer")]
    public required string Signer { get; init; }

    [JsonPropertyName("taker")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Taker { get; init; }

    [JsonPropertyName("tokenId")]
    public required string TokenId { get; init; }

    [JsonPropertyName("makerAmount")]
    public required string MakerAmount { get; init; }

    [JsonPropertyName("takerAmount")]
    public required string TakerAmount { get; init; }

    [JsonPropertyName("expiration")]
    public required string Expiration { get; init; }

    [JsonPropertyName("nonce")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Nonce { get; init; }

    [JsonPropertyName("feeRateBps")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FeeRateBps { get; init; }

    [JsonPropertyName("side")]
    public required string Side { get; init; }

    [JsonPropertyName("signatureType")]
    public required int SignatureType { get; init; }

    [JsonPropertyName("signature")]
    public required string Signature { get; init; }

    [JsonPropertyName("timestamp")]
    public required string Timestamp { get; init; }

    [JsonPropertyName("metadata")]
    public required string Metadata { get; init; }

    [JsonPropertyName("builder")]
    public required string Builder { get; init; }
}

/// <summary>
/// Writes a decimal string as a JSON number without parsing through fixed-width numeric types.
/// </summary>
public sealed class DecimalStringJsonNumberConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Number => Validate(ReadRawNumber(ref reader)),
            JsonTokenType.String => Validate(reader.GetString()),
            _ => throw new JsonException("Expected a decimal JSON number or string.")
        };
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteRawValue(Validate(value));
    }

    private static string Validate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new JsonException("Decimal number string cannot be empty.");
        }

        if (value.Length > 1 && value[0] == '0')
        {
            throw new JsonException("Decimal number string must be canonical and cannot contain leading zeroes.");
        }

        foreach (var c in value)
        {
            if (c < '0' || c > '9')
            {
                throw new JsonException("Decimal number string must contain only digits.");
            }
        }

        return value;
    }

    private static string ReadRawNumber(ref Utf8JsonReader reader)
    {
        return reader.HasValueSequence
            ? Encoding.UTF8.GetString(reader.ValueSequence.ToArray())
            : Encoding.UTF8.GetString(reader.ValueSpan);
    }
}

/// <summary>
/// 取消订单请求参数。
/// </summary>
public sealed record CancelOrderRequest
{
    [JsonPropertyName("orderID")]
    public required string OrderId { get; init; }
}

/// <summary>
/// 取消订单响应。
/// </summary>
public sealed record CancelOrderResponse
{
    [JsonPropertyName("canceled")]
    public IReadOnlyList<string>? Canceled { get; init; }

    [JsonPropertyName("not_canceled")]
    public Dictionary<string, string>? NotCanceled { get; init; }
}

/// <summary>
/// 取消所有订单请求。
/// </summary>
public sealed record CancelAllOrdersRequest
{
    [JsonPropertyName("market")]
    public string? Market { get; init; }

    [JsonPropertyName("asset_id")]
    public string? AssetId { get; init; }
}
