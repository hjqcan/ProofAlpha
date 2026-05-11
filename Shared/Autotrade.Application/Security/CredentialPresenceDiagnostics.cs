namespace Autotrade.Application.Security;

public sealed record RequiredCredentialField(string Path, string EvidenceKey);

public sealed record CredentialPresenceReport(
    bool AllPresent,
    IReadOnlyDictionary<string, string> Evidence,
    IReadOnlyList<string> MissingFields);

public static class CredentialPresenceDiagnostics
{
    public static readonly IReadOnlyList<RequiredCredentialField> PolymarketClobFields =
    [
        new("Polymarket:Clob:Address", "address"),
        new("Polymarket:Clob:PrivateKey", "privateKey"),
        new("Polymarket:Clob:ApiKey", "apiKey"),
        new("Polymarket:Clob:ApiSecret", "apiSecret")
    ];

    public static CredentialPresenceReport Evaluate(
        IReadOnlyList<RequiredCredentialField> fields,
        Func<string, string?> valueAccessor)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(valueAccessor);

        var evidence = new Dictionary<string, string>(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var field in fields)
        {
            var present = !string.IsNullOrWhiteSpace(valueAccessor(field.Path));
            evidence[field.EvidenceKey] = present ? "present" : "missing";
            if (!present)
            {
                missing.Add(field.Path);
            }
        }

        return new CredentialPresenceReport(
            missing.Count == 0,
            evidence,
            missing);
    }
}
