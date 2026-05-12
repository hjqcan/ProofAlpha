using System.Text.Json;
using System.Text.Json.Serialization;
using Autotrade.Application.Readiness;

namespace Autotrade.Application.Tests.Readiness;

public sealed class FirstRunReadinessContractTests
{
    private static readonly string[] ExpectedCheckIds =
    [
        "runtime.configuration.loaded",
        "runtime.modules.inventory",
        "database.connection",
        "database.migrations.current",
        "api.control_room.reachable",
        "market_data.public_api.reachable",
        "market_data.websocket.healthy",
        "background_jobs.heartbeats.fresh",
        "account_sync.configured",
        "compliance.geo_kyc.confirmed",
        "execution.mode.paper_default",
        "execution.live_armed",
        "risk.limits.configured",
        "credentials.exchange.present",
        "polymarket.builder_attribution.ready"
    ];

    [Fact]
    public void Create_ReturnsStableMachineReadableContract()
    {
        var contract = FirstRunReadinessContract.Create();

        Assert.Equal(FirstRunReadinessContract.ContractVersion, contract.ContractVersion);
        Assert.Equal("Autotrade first-run readiness", contract.Product);
        Assert.Equal(ExpectedCheckIds, contract.Checks.Select(check => check.Id).ToArray());
        Assert.All(contract.Checks, check =>
        {
            Assert.False(string.IsNullOrWhiteSpace(check.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(check.Description));
            Assert.False(string.IsNullOrWhiteSpace(check.ReadyCriteria));
            Assert.False(string.IsNullOrWhiteSpace(check.RemediationHint));
            Assert.NotEmpty(check.Sources);
        });
    }

    [Fact]
    public void Create_CoversFirstRunRequiredDiagnosticAreas()
    {
        var categories = FirstRunReadinessContract.Create()
            .Checks
            .Select(check => check.Category)
            .ToHashSet();

        Assert.Contains(ReadinessCheckCategory.Database, categories);
        Assert.Contains(ReadinessCheckCategory.Runtime, categories);
        Assert.Contains(ReadinessCheckCategory.Migrations, categories);
        Assert.Contains(ReadinessCheckCategory.Api, categories);
        Assert.Contains(ReadinessCheckCategory.MarketData, categories);
        Assert.Contains(ReadinessCheckCategory.AccountSync, categories);
        Assert.Contains(ReadinessCheckCategory.Compliance, categories);
        Assert.Contains(ReadinessCheckCategory.ExecutionMode, categories);
        Assert.Contains(ReadinessCheckCategory.RiskLimits, categories);
        Assert.Contains(ReadinessCheckCategory.BackgroundJobs, categories);
        Assert.Contains(ReadinessCheckCategory.WebSocket, categories);
        Assert.Contains(ReadinessCheckCategory.Credentials, categories);
    }

    [Fact]
    public void Create_SeparatesRequiredOptionalAndLiveOnlyChecks()
    {
        var checks = FirstRunReadinessContract.Create().Checks;

        Assert.Contains(checks, check => check.Requirement == ReadinessCheckRequirement.Required);
        Assert.Contains(checks, check => check.Requirement == ReadinessCheckRequirement.Optional);
        Assert.Contains(checks, check => check.Requirement == ReadinessCheckRequirement.LiveOnly);

        Assert.All(
            checks.Where(check => check.Requirement == ReadinessCheckRequirement.Required),
            check =>
            {
                var expected = check.Id == "execution.mode.paper_default"
                    ? new[] { ReadinessCapability.PaperTrading }
                    : new[] { ReadinessCapability.PaperTrading, ReadinessCapability.LiveTrading };
                Assert.Equal(expected, check.RequiredFor);
            });
        Assert.All(
            checks.Where(check => check.Requirement == ReadinessCheckRequirement.Optional),
            check => Assert.Empty(check.RequiredFor));
        Assert.All(
            checks.Where(check => check.Requirement == ReadinessCheckRequirement.LiveOnly),
            check => Assert.Equal(new[] { ReadinessCapability.LiveTrading }, check.RequiredFor));
    }

    [Fact]
    public void Create_DefinesPaperAndLiveCapabilityRequirements()
    {
        var contract = FirstRunReadinessContract.Create();
        var paper = Assert.Single(
            contract.Capabilities,
            capability => capability.Capability == ReadinessCapability.PaperTrading);
        var live = Assert.Single(
            contract.Capabilities,
            capability => capability.Capability == ReadinessCapability.LiveTrading);

        Assert.Contains("execution.mode.paper_default", paper.RequiredCheckIds);
        Assert.DoesNotContain("execution.live_armed", paper.RequiredCheckIds);
        Assert.DoesNotContain("credentials.exchange.present", paper.RequiredCheckIds);

        Assert.DoesNotContain("execution.mode.paper_default", live.RequiredCheckIds);
        Assert.Contains("execution.live_armed", live.RequiredCheckIds);
        Assert.Contains("credentials.exchange.present", live.RequiredCheckIds);
        Assert.Contains("compliance.geo_kyc.confirmed", live.RequiredCheckIds);
        Assert.Contains("account_sync.configured", live.RequiredCheckIds);
    }

    [Fact]
    public void JsonContract_UsesCamelCaseAndStringEnums()
    {
        var json = JsonSerializer.Serialize(
            FirstRunReadinessContract.Create(),
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter() }
            });

        Assert.Contains("\"contractVersion\"", json, StringComparison.Ordinal);
        Assert.Contains("\"category\":\"Database\"", json, StringComparison.Ordinal);
        Assert.Contains("\"requirement\":\"LiveOnly\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ContractVersion\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"category\":1", json, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonContract_DoesNotExposeSecretValueFields()
    {
        var json = JsonSerializer.Serialize(
            FirstRunReadinessContract.Create(),
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter() }
            });

        Assert.DoesNotContain("privateKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secretValue", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReportFactory_BlocksPaperWhenRequiredCheckIsUnhealthy()
    {
        var now = DateTimeOffset.Parse("2026-05-03T10:00:00Z");
        var report = ReadinessReportFactory.Create(
            now,
            new Dictionary<string, ReadinessCheckProbe>
            {
                ["runtime.configuration.loaded"] = ReadyProbe(now),
                ["runtime.modules.inventory"] = ReadyProbe(now),
                ["database.connection"] = new(
                    ReadinessCheckStatus.Unhealthy,
                    "test",
                    "Database is unavailable.",
                    now),
                ["database.migrations.current"] = ReadyProbe(now),
                ["api.control_room.reachable"] = ReadyProbe(now),
                ["execution.mode.paper_default"] = ReadyProbe(now),
                ["risk.limits.configured"] = ReadyProbe(now)
            });

        Assert.Equal(ReadinessOverallStatus.Blocked, report.Status);
        var paper = Assert.Single(
            report.Capabilities,
            capability => capability.Capability == ReadinessCapability.PaperTrading);
        Assert.Equal(ReadinessOverallStatus.Blocked, paper.Status);
        Assert.Contains("database.connection", paper.BlockingCheckIds);
    }

    [Fact]
    public void ReportFactory_DegradesOverallWhenOnlyLiveCapabilityIsBlocked()
    {
        var now = DateTimeOffset.Parse("2026-05-03T10:00:00Z");
        var probes = FirstRunReadinessContract.Create()
            .Checks
            .ToDictionary(
                check => check.Id,
                check => check.Requirement == ReadinessCheckRequirement.LiveOnly
                    ? new ReadinessCheckProbe(ReadinessCheckStatus.Blocked, "test", "Live prerequisite missing.", now)
                    : ReadyProbe(now));

        var report = ReadinessReportFactory.Create(now, probes);

        Assert.Equal(ReadinessOverallStatus.Degraded, report.Status);
        var paper = Assert.Single(
            report.Capabilities,
            capability => capability.Capability == ReadinessCapability.PaperTrading);
        var live = Assert.Single(
            report.Capabilities,
            capability => capability.Capability == ReadinessCapability.LiveTrading);
        Assert.Equal(ReadinessOverallStatus.Ready, paper.Status);
        Assert.Equal(ReadinessOverallStatus.Blocked, live.Status);
        Assert.Contains("execution.live_armed", live.BlockingCheckIds);
    }

    private static ReadinessCheckProbe ReadyProbe(DateTimeOffset checkedAtUtc)
    {
        return new ReadinessCheckProbe(
            ReadinessCheckStatus.Ready,
            "test",
            "Ready.",
            checkedAtUtc);
    }
}
