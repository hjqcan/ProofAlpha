using Autotrade.Api.Controllers;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Tests;

public sealed class RiskDrilldownControllerContractTests
{
    [Fact]
    public async Task GetRiskEventReturnsDetailForKnownEvent()
    {
        var riskEventId = Guid.NewGuid();
        var controller = new RiskDrilldownController(new StubRiskDrilldownService(CreateDrilldown(riskEventId)));

        var result = await controller.GetRiskEvent(riskEventId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<RiskEventDrilldown>(ok.Value);
        Assert.Equal(riskEventId, response.Event.Id);
    }

    [Fact]
    public async Task GetRiskEventReturnsNotFoundForMissingEvent()
    {
        var controller = new RiskDrilldownController(new StubRiskDrilldownService(null));

        var result = await controller.GetRiskEvent(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task QueryUnhedgedExposuresPassesFilters()
    {
        var riskEventId = Guid.NewGuid();
        var service = new StubRiskDrilldownService(null);
        var controller = new RiskDrilldownController(service);
        var fromUtc = DateTimeOffset.Parse("2026-05-03T12:00:00Z");
        var toUtc = fromUtc.AddHours(1);

        var result = await controller.QueryUnhedgedExposures(
            "strategy-main",
            "market-1",
            riskEventId,
            fromUtc,
            toUtc,
            25,
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<UnhedgedExposureDrilldownResponse>(ok.Value);
        Assert.Equal(25, response.Limit);
        Assert.Equal("strategy-main", service.LastQuery?.StrategyId);
        Assert.Equal("market-1", service.LastQuery?.MarketId);
        Assert.Equal(riskEventId, service.LastQuery?.RiskEventId);
        Assert.Equal(fromUtc, service.LastQuery?.FromUtc);
        Assert.Equal(toUtc, service.LastQuery?.ToUtc);
    }

    [Fact]
    public async Task ExportRiskEventCsvUsesRiskDrilldownFormatter()
    {
        var riskEventId = Guid.NewGuid();
        var controller = new RiskDrilldownController(new StubRiskDrilldownService(CreateDrilldown(riskEventId)));

        var result = await controller.ExportRiskEventCsv(riskEventId, CancellationToken.None);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal("text/csv", content.ContentType);
        Assert.Contains("risk_events", content.Content);
        Assert.Contains(riskEventId.ToString(), content.Content);
    }

    private static RiskEventDrilldown CreateDrilldown(Guid riskEventId)
        => new(
            DateTimeOffset.UtcNow,
            new RiskEventRecord(
                riskEventId,
                "RISK_UNHEDGED_TIMEOUT_FORCE_HEDGE",
                RiskSeverity.Critical,
                "unhedged",
                "strategy-main",
                "{}",
                DateTimeOffset.UtcNow,
                "market-1"),
            new RiskTriggerDrilldown("unhedged", "Unhedged exposure", 120m, 60m, "seconds", "breached"),
            new RiskActionDrilldown("ForceHedge", "Hedge order submitted", "RISK_UNHEDGED_TIMEOUT_FORCE_HEDGE"),
            [new RiskAffectedOrder(Guid.NewGuid(), "client-1", "strategy-main", "market-1", "Cancelled", "orders", "orders/1")],
            null,
            null,
            new RiskDrilldownSourceReferences(
                $"/api/control-room/risk/events/{riskEventId}",
                $"/api/control-room/risk/events/{riskEventId}/csv",
                [riskEventId],
                []));

    private sealed class StubRiskDrilldownService(RiskEventDrilldown? drilldown) : IRiskDrilldownService
    {
        public RiskDrilldownQuery? LastQuery { get; private set; }

        public Task<RiskEventDrilldown?> GetRiskEventAsync(
            Guid riskEventId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(drilldown?.Event.Id == riskEventId ? drilldown : null);

        public Task<UnhedgedExposureDrilldownResponse> QueryUnhedgedExposuresAsync(
            RiskDrilldownQuery query,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult(new UnhedgedExposureDrilldownResponse(
                DateTimeOffset.UtcNow,
                0,
                query.Limit,
                query,
                []));
        }
    }
}
