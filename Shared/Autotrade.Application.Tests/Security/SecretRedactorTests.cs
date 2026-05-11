using Autotrade.Application.Security;

namespace Autotrade.Application.Tests.Security;

public sealed class SecretRedactorTests
{
    [Fact]
    public void Redact_RemovesJsonAndKeyValueSecrets()
    {
        var input = """
            {"apiSecret":"secret-value","nested":{"privateKey":"0xabcdef"}}
            ApiKey=plain-api-key; Password=db-password;
            """;

        var redacted = SecretRedactor.Redact(input);

        Assert.Contains("\"apiSecret\":\"***REDACTED***\"", redacted, StringComparison.Ordinal);
        Assert.Contains("\"privateKey\":\"***REDACTED***\"", redacted, StringComparison.Ordinal);
        Assert.Contains("ApiKey=***REDACTED***", redacted, StringComparison.Ordinal);
        Assert.Contains("Password=***REDACTED***", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("0xabcdef", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("plain-api-key", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("db-password", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_RemovesBearerMaterial()
    {
        var redacted = SecretRedactor.Redact(
            "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9 and Bearer abcdefghijklmnop");

        Assert.Contains("Authorization: Bearer ***REDACTED***", redacted, StringComparison.Ordinal);
        Assert.Contains("Bearer ***REDACTED***", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbGci", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("abcdefghijklmnop", redacted, StringComparison.Ordinal);
    }
}
