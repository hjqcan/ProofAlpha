using Autotrade.Api.Controllers;
using Autotrade.Strategy.Application.Audit;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Tests;

public sealed class ReplayExportsControllerContractTests
{
    [Fact]
    public async Task ExportForwardsReplayQueryAndReturnsPackage()
    {
        var expected = CreatePackage();
        var replayExportService = new StubReplayExportService(expected);
        var controller = new ReplayExportsController(replayExportService);
        var orderId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var sessionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var riskEventId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var fromUtc = DateTimeOffset.Parse("2026-05-03T12:00:00Z");
        var toUtc = fromUtc.AddHours(1);

        var result = await controller.Export(
            "strategy-main",
            "market-1",
            orderId,
            "client-1",
            sessionId,
            riskEventId,
            "corr-1",
            fromUtc,
            toUtc,
            250,
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ReplayExportPackage>(ok.Value);
        Assert.Same(expected, response);
        Assert.Equal(new ReplayExportQuery(
            "strategy-main",
            "market-1",
            orderId,
            "client-1",
            sessionId,
            riskEventId,
            "corr-1",
            fromUtc,
            toUtc,
            250), replayExportService.LastQuery);
    }

    [Fact]
    public async Task ExportClampsLimitToReplayContractBounds()
    {
        var replayExportService = new StubReplayExportService(CreatePackage());
        var controller = new ReplayExportsController(replayExportService);

        await controller.Export(
            strategyId: null,
            marketId: null,
            orderId: null,
            clientOrderId: null,
            runSessionId: null,
            riskEventId: null,
            correlationId: null,
            fromUtc: null,
            toUtc: null,
            limit: 50_000,
            cancellationToken: CancellationToken.None);

        Assert.Equal(5000, replayExportService.LastQuery?.Limit);
    }

    private static ReplayExportPackage CreatePackage()
        => new(
            DateTimeOffset.Parse("2026-05-03T12:00:00Z"),
            ReplayExportService.ContractVersion,
            new ReplayExportQuery(Limit: 100),
            new ReplayRedactionSummary([], []),
            [],
            null,
            new AuditTimeline(
                DateTimeOffset.Parse("2026-05-03T12:00:00Z"),
                0,
                100,
                new AuditTimelineQuery(),
                []),
            new ReplayEvidenceBundle([], [], [], [], [], []),
            [],
            null,
            new ReplayExportReferences(
                "/api/replay-exports?limit=100",
                "autotrade export replay-package --json --limit 100",
                "docs/operations/replay-export-schema.md"));

    private sealed class StubReplayExportService(ReplayExportPackage package) : IReplayExportService
    {
        public ReplayExportQuery? LastQuery { get; private set; }

        public Task<ReplayExportPackage> ExportAsync(
            ReplayExportQuery query,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult(package);
        }
    }
}
