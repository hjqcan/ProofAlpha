using Autotrade.Application.Security;

namespace Autotrade.Application.Tests.Security;

public sealed class CredentialPresenceDiagnosticsTests
{
    [Fact]
    public void Evaluate_ReportsPresenceWithoutReturningCredentialValues()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Polymarket:Clob:Address"] = "0x1234567890abcdef1234567890abcdef12345678",
            ["Polymarket:Clob:PrivateKey"] = "private-key",
            ["Polymarket:Clob:ApiKey"] = "api-key",
            ["Polymarket:Clob:ApiSecret"] = "api-secret"
        };

        var report = CredentialPresenceDiagnostics.Evaluate(
            CredentialPresenceDiagnostics.PolymarketClobFields,
            path => values.GetValueOrDefault(path));

        Assert.True(report.AllPresent);
        Assert.Empty(report.MissingFields);
        Assert.All(report.Evidence.Values, value => Assert.Equal("present", value));
        Assert.DoesNotContain("private-key", report.Evidence.Values, StringComparer.Ordinal);
        Assert.DoesNotContain("api-secret", report.Evidence.Values, StringComparer.Ordinal);
    }

    [Fact]
    public void Evaluate_ReportsMissingFieldsWithoutReturningPresentCredentialValues()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Polymarket:Clob:Address"] = "0x1234567890abcdef1234567890abcdef12345678",
            ["Polymarket:Clob:ApiKey"] = "api-key"
        };

        var report = CredentialPresenceDiagnostics.Evaluate(
            CredentialPresenceDiagnostics.PolymarketClobFields,
            path => values.GetValueOrDefault(path));

        Assert.False(report.AllPresent);
        Assert.Contains("Polymarket:Clob:PrivateKey", report.MissingFields);
        Assert.Contains("Polymarket:Clob:ApiSecret", report.MissingFields);
        Assert.Equal("present", report.Evidence["address"]);
        Assert.Equal("missing", report.Evidence["privateKey"]);
        Assert.Equal("present", report.Evidence["apiKey"]);
        Assert.Equal("missing", report.Evidence["apiSecret"]);
        Assert.DoesNotContain("api-key", report.Evidence.Values, StringComparer.Ordinal);
    }
}
