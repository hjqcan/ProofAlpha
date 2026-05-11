using Autotrade.Api.Controllers;
using Autotrade.Strategy.Application.Audit;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Tests;

public sealed class AuditTimelineControllerContractTests
{
    [Fact]
    public async Task QueryPassesFiltersAndReturnsTimeline()
    {
        var runSessionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var orderId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var riskEventId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var fromUtc = DateTimeOffset.Parse("2026-05-03T12:00:00Z");
        var toUtc = fromUtc.AddHours(1);
        var service = new StubAuditTimelineService();
        var controller = new AuditTimelineController(service);

        var result = await controller.Query(
            "strategy-main",
            "market-1",
            orderId,
            "client-1",
            runSessionId,
            riskEventId,
            "corr-1",
            fromUtc,
            toUtc,
            75,
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AuditTimeline>(ok.Value);
        Assert.Equal(1, response.Count);
        Assert.NotNull(service.LastQuery);
        Assert.Equal("strategy-main", service.LastQuery!.StrategyId);
        Assert.Equal("market-1", service.LastQuery.MarketId);
        Assert.Equal(orderId, service.LastQuery.OrderId);
        Assert.Equal("client-1", service.LastQuery.ClientOrderId);
        Assert.Equal(runSessionId, service.LastQuery.RunSessionId);
        Assert.Equal(riskEventId, service.LastQuery.RiskEventId);
        Assert.Equal("corr-1", service.LastQuery.CorrelationId);
        Assert.Equal(fromUtc, service.LastQuery.FromUtc);
        Assert.Equal(toUtc, service.LastQuery.ToUtc);
        Assert.Equal(75, service.LastQuery.Limit);
    }

    private sealed class StubAuditTimelineService : IAuditTimelineService
    {
        public AuditTimelineQuery? LastQuery { get; private set; }

        public Task<AuditTimeline> QueryAsync(
            AuditTimelineQuery query,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            var item = new AuditTimelineItem(
                Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                DateTimeOffset.Parse("2026-05-03T12:30:00Z"),
                AuditTimelineItemType.RiskEvent,
                "Risk",
                "strategy-main",
                "Warning: RISK_TEST - test",
                "risk-events/dddddddd-dddd-dddd-dddd-dddddddddddd",
                "strategy-main",
                "market-1",
                query.OrderId,
                query.ClientOrderId,
                query.RunSessionId,
                query.RiskEventId,
                query.CorrelationId,
                "{}");

            return Task.FromResult(new AuditTimeline(
                DateTimeOffset.UtcNow,
                1,
                query.Limit,
                query,
                [item]));
        }
    }
}
