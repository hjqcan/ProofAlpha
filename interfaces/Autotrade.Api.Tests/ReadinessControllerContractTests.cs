using System.Text.Json;
using System.Text.Json.Serialization;
using Autotrade.Api.Controllers;
using Autotrade.Application.Readiness;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Tests;

public sealed class ReadinessControllerContractTests
{
    [Fact]
    public void GetContractReturnsSharedFirstRunWizardContract()
    {
        var controller = new ReadinessController(new FakeReadinessReportService());

        var result = controller.GetContract();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var contract = Assert.IsType<FirstRunWizardContract>(ok.Value);
        Assert.Equal(FirstRunReadinessContract.ContractVersion, contract.ContractVersion);
        Assert.Contains(contract.Checks, check => check.Id == "runtime.modules.inventory");
        Assert.Contains(contract.Checks, check => check.Id == "database.connection");
        Assert.Contains(contract.Checks, check => check.Id == "execution.live_armed");
        Assert.Contains(contract.Capabilities, capability => capability.Capability == ReadinessCapability.PaperTrading);
        Assert.Contains(contract.Capabilities, capability => capability.Capability == ReadinessCapability.LiveTrading);
    }

    [Fact]
    public async Task GetReturnsReadinessReportEnvelope()
    {
        var now = DateTimeOffset.Parse("2026-05-03T10:00:00Z");
        var report = ReadinessReportFactory.Create(
            now,
            new Dictionary<string, ReadinessCheckProbe>
            {
                ["runtime.configuration.loaded"] = Ready(now),
                ["runtime.modules.inventory"] = Ready(now),
                ["database.connection"] = Ready(now),
                ["database.migrations.current"] = Ready(now),
                ["api.control_room.reachable"] = Ready(now),
                ["execution.mode.paper_default"] = Ready(now),
                ["background_jobs.heartbeats.fresh"] = Ready(now),
                ["risk.limits.configured"] = Ready(now)
            });
        var controller = new ReadinessController(new FakeReadinessReportService(report));

        var result = await controller.Get(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ReadinessReport>(ok.Value);
        Assert.Equal(FirstRunReadinessContract.ContractVersion, response.ContractVersion);
        Assert.Contains(response.Checks, check => check.Id == "api.control_room.reachable");
        Assert.Contains(response.Capabilities, capability => capability.Capability == ReadinessCapability.LiveTrading);
    }

    [Fact]
    public void ReadinessReportSerializesWithCamelCaseAndStringEnums()
    {
        var now = DateTimeOffset.Parse("2026-05-03T10:00:00Z");
        var report = ReadinessReportFactory.Create(
            now,
            new Dictionary<string, ReadinessCheckProbe>
            {
                ["runtime.configuration.loaded"] = Ready(now),
                ["runtime.modules.inventory"] = Ready(now),
                ["database.connection"] = Ready(now),
                ["database.migrations.current"] = Ready(now),
                ["api.control_room.reachable"] = Ready(now),
                ["execution.mode.paper_default"] = Ready(now),
                ["background_jobs.heartbeats.fresh"] = Ready(now),
                ["risk.limits.configured"] = Ready(now)
            });

        var json = JsonSerializer.Serialize(
            report,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter() }
            });

        Assert.Contains("\"checkedAtUtc\"", json, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"Degraded\"", json, StringComparison.Ordinal);
        Assert.Contains("\"category\":\"Api\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Status\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"category\":3", json, StringComparison.Ordinal);
    }

    private static ReadinessCheckProbe Ready(DateTimeOffset checkedAtUtc)
    {
        return new ReadinessCheckProbe(
            ReadinessCheckStatus.Ready,
            "test",
            "Ready.",
            checkedAtUtc);
    }

    private sealed class FakeReadinessReportService(ReadinessReport? report = null) : IReadinessReportService
    {
        public Task<ReadinessReport> GetReportAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(report ?? ReadinessReportFactory.Create(
                DateTimeOffset.Parse("2026-05-03T10:00:00Z"),
                new Dictionary<string, ReadinessCheckProbe>()));
        }
    }
}
