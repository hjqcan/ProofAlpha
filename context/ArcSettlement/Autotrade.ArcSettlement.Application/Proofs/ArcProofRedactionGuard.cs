using System.Text.Json;
using System.Text.RegularExpressions;
using Autotrade.ArcSettlement.Application.Contract.Proofs;

namespace Autotrade.ArcSettlement.Application.Proofs;

public interface IArcProofRedactionGuard
{
    void ValidatePublicProof<T>(T document);
}

public sealed partial class ArcProofRedactionGuard : IArcProofRedactionGuard
{
    public const string RedactedText = "***REDACTED***";

    private static readonly string[] SensitiveNameFragments =
    [
        "privatekey",
        "private_key",
        "apikey",
        "api_key",
        "apisecret",
        "api_secret",
        "apipassphrase",
        "api_passphrase",
        "passphrase",
        "mnemonic",
        "seedphrase",
        "seed_phrase",
        "clobsignature",
        "clob_signature",
        "ordersignature",
        "order_signature"
    ];

    public void ValidatePublicProof<T>(T document)
    {
        var json = ArcProofJson.SerializeStable(document);
        if (JsonSecretPattern().IsMatch(json)
            || AuthorizationBearerPattern().IsMatch(json)
            || StandaloneBearerPattern().IsMatch(json)
            || KeyValueSecretPattern().IsMatch(json))
        {
            throw new ArcProofRedactionException("Proof document contains a field that matches secret redaction rules.");
        }

        using var parsed = JsonDocument.Parse(json);
        var sensitivePaths = new List<string>();
        Scan(parsed.RootElement, "$", sensitivePaths);

        if (sensitivePaths.Count > 0)
        {
            throw new ArcProofRedactionException(
                $"Proof document contains unredacted sensitive fields: {string.Join(", ", sensitivePaths)}.");
        }

        if (LikelyPrivateKeyMaterialPattern().IsMatch(json) || LikelyMnemonicMaterialPattern().IsMatch(json))
        {
            throw new ArcProofRedactionException("Proof document contains likely secret material.");
        }
    }

    private static void Scan(JsonElement element, string path, List<string> sensitivePaths)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var childPath = $"{path}.{property.Name}";
                    if (IsSensitiveName(property.Name) && IsUnredactedValue(property.Value))
                    {
                        sensitivePaths.Add(childPath);
                    }

                    Scan(property.Value, childPath, sensitivePaths);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Scan(item, $"{path}[{index}]", sensitivePaths);
                    index++;
                }

                break;
        }
    }

    private static bool IsSensitiveName(string name)
    {
        var normalized = name.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return SensitiveNameFragments.Any(fragment => normalized.Contains(fragment, StringComparison.Ordinal));
    }

    private static bool IsUnredactedValue(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => !IsRedacted(value.GetString()),
            JsonValueKind.Null => false,
            JsonValueKind.Undefined => false,
            JsonValueKind.Array => value.EnumerateArray().Any(IsUnredactedValue),
            JsonValueKind.Object => value.EnumerateObject().Any(property => IsUnredactedValue(property.Value)),
            _ => true
        };

    private static bool IsRedacted(string? value)
        => string.IsNullOrWhiteSpace(value)
           || value.Contains(RedactedText, StringComparison.Ordinal)
           || value.Contains("redacted", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(
        "(?<prefix>\"(?:[^\"]*(?:privateKey|private_key|apiKey|api_key|apiSecret|api_secret|apiPassphrase|api_passphrase|password|accessToken|access_token|refreshToken|refresh_token|authorization|bearer)[^\"]*)\"\\s*:\\s*)\"(?:\\\\.|[^\"\\\\])*\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JsonSecretPattern();

    [GeneratedRegex(
        "(?<prefix>\\b(?:privateKey|private_key|apiKey|api_key|apiSecret|api_secret|apiPassphrase|api_passphrase|password|accessToken|access_token|refreshToken|refresh_token)\\b\\s*[=:]\\s*)[^\\s;,}\\]]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeyValueSecretPattern();

    [GeneratedRegex(
        "(?<prefix>\\bAuthorization\\s*[=:]\\s*Bearer\\s+)[^\\s;,}\\]]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationBearerPattern();

    [GeneratedRegex(
        "(?<prefix>\\bBearer\\s+)[A-Za-z0-9._~+/=-]{8,}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StandaloneBearerPattern();

    [GeneratedRegex("\\b(?:private\\s*key|private_key)\\b.{0,32}\\b0x[a-fA-F0-9]{64}\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LikelyPrivateKeyMaterialPattern();

    [GeneratedRegex("\\b(?:mnemonic|seed\\s*phrase|seedphrase)\\b\\s*[=:]\\s*(?!\\*\\*\\*REDACTED\\*\\*\\*|redacted\\b)[^\\s;,}\\]]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LikelyMnemonicMaterialPattern();
}

public sealed class ArcProofRedactionException(string message) : InvalidOperationException(message);
