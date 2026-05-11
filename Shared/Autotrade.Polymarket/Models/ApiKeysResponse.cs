using System.Text.Json.Serialization;

namespace Autotrade.Polymarket.Models;

/// <summary>
/// /auth/api-keys 返回结构。
/// </summary>
public sealed class ApiKeysResponse
{
    [JsonPropertyName("apiKeys")]
    public List<ApiKeyRaw> ApiKeys { get; init; } = [];
}

