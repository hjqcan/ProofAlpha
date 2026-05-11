using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nethereum.Signer;
using Nethereum.Signer.EIP712;

namespace Autotrade.Polymarket.Http;

/// <summary>
/// Polymarket CLOB 鉴权头生成器（L1=EIP-712，L2=HMAC）。
/// </summary>
public static class PolymarketAuthHeaderFactory
{
    private static readonly JsonSerializerOptions TypedDataJsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    public static string GetAddressFromPrivateKey(string privateKey)
    {
        var key = new EthECKey(NormalizePrivateKey(privateKey));
        return key.GetPublicAddress();
    }

    public static Dictionary<string, string> CreateL1Headers(
        string privateKey,
        int chainId,
        int? nonce = null,
        long? timestampSeconds = null)
    {
        var ts = timestampSeconds ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var n = nonce ?? 0;

        var address = GetAddressFromPrivateKey(privateKey);
        var signature = BuildClobEip712Signature(privateKey, chainId, address, ts, n);

        return new Dictionary<string, string>
        {
            [PolymarketConstants.PolyAddressHeader] = address,
            [PolymarketConstants.PolySignatureHeader] = signature,
            [PolymarketConstants.PolyTimestampHeader] = ts.ToString(),
            [PolymarketConstants.PolyNonceHeader] = n.ToString()
        };
    }

    public static Dictionary<string, string> CreateL2Headers(
        string address,
        string apiKey,
        string apiSecretBase64,
        string apiPassphrase,
        HttpMethod method,
        string requestPath,
        string? serializedBody,
        long? timestampSeconds = null)
    {
        var ts = timestampSeconds ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var signature = BuildPolyHmacSignature(
            apiSecretBase64,
            ts,
            method.Method,
            requestPath,
            serializedBody);

        return new Dictionary<string, string>
        {
            [PolymarketConstants.PolyAddressHeader] = address,
            [PolymarketConstants.PolySignatureHeader] = signature,
            [PolymarketConstants.PolyTimestampHeader] = ts.ToString(),
            [PolymarketConstants.PolyApiKeyHeader] = apiKey,
            [PolymarketConstants.PolyPassphraseHeader] = apiPassphrase
        };
    }

    /// <summary>
    /// 构造 canonical HMAC 签名（与官方 clob-client 行为一致）：
    /// message = timestamp + method + requestPath + body?
    /// HMAC-SHA256(key=base64(secret))，输出 base64，再做 URL-safe（+ -> -, / -> _，保留 =）。
    /// </summary>
    public static string BuildPolyHmacSignature(
        string apiSecretBase64,
        long timestampSeconds,
        string method,
        string requestPath,
        string? serializedBody)
    {
        if (string.IsNullOrWhiteSpace(apiSecretBase64))
        {
            throw new ArgumentException("API Secret 不能为空", nameof(apiSecretBase64));
        }

        if (string.IsNullOrWhiteSpace(method))
        {
            throw new ArgumentException("method 不能为空", nameof(method));
        }

        if (string.IsNullOrWhiteSpace(requestPath))
        {
            throw new ArgumentException("requestPath 不能为空", nameof(requestPath));
        }

        var msg = serializedBody is null
            ? $"{timestampSeconds}{method}{requestPath}"
            : $"{timestampSeconds}{method}{requestPath}{serializedBody}";

        var secretBytes = Convert.FromBase64String(apiSecretBase64.Trim());
        var msgBytes = Encoding.UTF8.GetBytes(msg);

        using var hmac = new HMACSHA256(secretBytes);
        var sigBytes = hmac.ComputeHash(msgBytes);

        var sig = Convert.ToBase64String(sigBytes);

        // NOTE: URL-safe base64，但保留 '=' 后缀（与官方实现一致）
        return sig.Replace("+", "-").Replace("/", "_");
    }

    /// <summary>
    /// 构造 L1 EIP-712 签名（ClobAuthDomain）。
    /// </summary>
    private static string BuildClobEip712Signature(
        string privateKey,
        int chainId,
        string address,
        long timestampSeconds,
        int nonce)
    {
        var typedData = new
        {
            types = new
            {
                EIP712Domain = new object[]
                {
                    new { name = "name", type = "string" },
                    new { name = "version", type = "string" },
                    new { name = "chainId", type = "uint256" }
                },
                ClobAuth = new object[]
                {
                    new { name = "address", type = "address" },
                    new { name = "timestamp", type = "string" },
                    new { name = "nonce", type = "uint256" },
                    new { name = "message", type = "string" }
                }
            },
            primaryType = "ClobAuth",
            domain = new
            {
                name = PolymarketConstants.ClobDomainName,
                version = PolymarketConstants.ClobDomainVersion,
                chainId = chainId
            },
            message = new
            {
                address,
                timestamp = timestampSeconds.ToString(),
                nonce,
                message = PolymarketConstants.ClobAuthMessageToSign
            }
        };

        var json = JsonSerializer.Serialize(typedData, TypedDataJsonOptions);
        var signer = new Eip712TypedDataSigner();
        // Nethereum.EIP712：使用 V4 签名（与 ethers.js _signTypedData 对齐）
        return signer.SignTypedDataV4(json, new EthECKey(NormalizePrivateKey(privateKey)));
    }

    private static string NormalizePrivateKey(string privateKey)
    {
        if (string.IsNullOrWhiteSpace(privateKey))
        {
            throw new ArgumentException("PrivateKey 不能为空", nameof(privateKey));
        }

        return privateKey.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? privateKey
            : $"0x{privateKey}";
    }
}

