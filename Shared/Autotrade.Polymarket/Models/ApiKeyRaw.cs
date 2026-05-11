using System.Text.Json.Serialization;

namespace Autotrade.Polymarket.Models;

/// <summary>
/// CLOB API Key 原始响应（字段名与服务端一致）。
/// </summary>
public sealed class ApiKeyRaw
{
    [JsonPropertyName("apiKey")]
    public string ApiKey { get; init; } = string.Empty;

    [JsonPropertyName("secret")]
    public string Secret { get; init; } = string.Empty;

    [JsonPropertyName("passphrase")]
    public string Passphrase { get; init; } = string.Empty;
}

