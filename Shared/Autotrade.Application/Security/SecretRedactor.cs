using System.Text.RegularExpressions;

namespace Autotrade.Application.Security;

public static partial class SecretRedactor
{
    public const string RedactedText = "***REDACTED***";

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        var redacted = JsonSecretPattern().Replace(
            value,
            match => $"{match.Groups["prefix"].Value}\"{RedactedText}\"");
        redacted = AuthorizationBearerPattern().Replace(
            redacted,
            match => $"{match.Groups["prefix"].Value}{RedactedText}");
        redacted = StandaloneBearerPattern().Replace(
            redacted,
            match => $"{match.Groups["prefix"].Value}{RedactedText}");
        redacted = KeyValueSecretPattern().Replace(
            redacted,
            match => $"{match.Groups["prefix"].Value}{RedactedText}");

        return redacted;
    }

    [GeneratedRegex(
        "(?<prefix>\"(?:[^\"]*(?:privateKey|private_key|apiKey|api_key|apiSecret|api_secret|apiPassphrase|api_passphrase|password|token|authorization|bearer)[^\"]*)\"\\s*:\\s*)\"(?:\\\\.|[^\"\\\\])*\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JsonSecretPattern();

    [GeneratedRegex(
        "(?<prefix>\\b(?:privateKey|private_key|apiKey|api_key|apiSecret|api_secret|apiPassphrase|api_passphrase|password|accessToken|access_token|refreshToken|refresh_token|token)\\b\\s*[=:]\\s*)[^\\s;,}\\]]+",
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
}
