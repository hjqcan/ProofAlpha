using Autotrade.Api.Controllers;
using Autotrade.Application.RunSessions;
using Autotrade.Strategy.Application.Promotion;
using Autotrade.Strategy.Application.RunReports;
using Autotrade.Strategy.Application.RunSessions;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Tests;

public sealed class RunReportsControllerContractTests
{
    [Fact]
    public async Task GetReturnsReportForKnownSession()
    {
        var sessionId = Guid.NewGuid();
        var report = CreateReport(sessionId);
        var controller = new RunReportsController(
            new StubRunReportService(report),
            new StubPromotionChecklistService(CreateChecklist(sessionId)),
            new StubRunSessionService(report.Session));

        var result = await controller.Get(sessionId, 50, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<PaperRunReport>(ok.Value);
        Assert.Equal(sessionId, response.Session.SessionId);
        Assert.Equal("Complete", response.ReportStatus);
    }

    [Fact]
    public async Task GetReturnsNotFoundForMissingSession()
    {
        var controller = new RunReportsController(
            new StubRunReportService(null),
            new StubPromotionChecklistService(null),
            new StubRunSessionService(null));

        var result = await controller.Get(Guid.NewGuid(), null, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetPromotionChecklistReturnsChecklistForKnownSession()
    {
        var sessionId = Guid.NewGuid();
        var controller = new RunReportsController(
            new StubRunReportService(CreateReport(sessionId)),
            new StubPromotionChecklistService(CreateChecklist(sessionId)),
            new StubRunSessionService(null));

        var result = await controller.GetPromotionChecklist(sessionId, 50, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<PaperPromotionChecklist>(ok.Value);
        Assert.Equal(sessionId, response.SessionId);
        Assert.True(response.LiveArmingUnchanged);
    }

    [Fact]
    public async Task GetPromotionChecklistReturnsNotFoundForMissingSession()
    {
        var controller = new RunReportsController(
            new StubRunReportService(null),
            new StubPromotionChecklistService(null),
            new StubRunSessionService(null));

        var result = await controller.GetPromotionChecklist(Guid.NewGuid(), null, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetActiveReturnsCurrentPaperRunSession()
    {
        var sessionId = Guid.NewGuid();
        var activeSession = CreateReport(sessionId).Session;
        var controller = new RunReportsController(
            new StubRunReportService(null),
            new StubPromotionChecklistService(null),
            new StubRunSessionService(activeSession));

        var result = await controller.GetActive(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<PaperRunSessionRecord>(ok.Value);
        Assert.Equal(sessionId, response.SessionId);
        Assert.True(response.IsActive);
    }

    [Fact]
    public async Task GetActiveReturnsNullWhenNoPaperRunSessionIsActive()
    {
        var controller = new RunReportsController(
            new StubRunReportService(null),
            new StubPromotionChecklistService(null),
            new StubRunSessionService(null));

        var result = await controller.GetActive(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Null(ok.Value);
    }

    private static PaperRunReport CreateReport(Guid sessionId)
        => new(
            DateTimeOffset.UtcNow,
            "Complete",
            [],
            new PaperRunSessionRecord(
                sessionId,
                "Paper",
                "cfg-v1",
                ["dual_leg_arbitrage"],
                "{}",
                "test",
                DateTimeOffset.UtcNow.AddHours(-1),
                null,
                null,
                true,
                false),
            new PaperRunReportSummary(1, 1, 1, 0, 0, 0, 0, 0, 0m, 0m, 0m, 0m, 0m),
            [],
            [],
            [],
            [],
            new PaperRunEvidenceLinks([], [], [], [], [], []),
            new PaperRunExportReferences(
                $"/api/run-reports/{sessionId}",
                $"autotrade export run-report --session-id {sessionId} --json",
                $"autotrade export run-report --session-id {sessionId}",
                ["summary"]));

    private static PaperPromotionChecklist CreateChecklist(Guid sessionId)
        => new(
            sessionId,
            DateTimeOffset.UtcNow,
            "Passed",
            true,
            true,
            [new PaperPromotionCriterion("run_duration", "Run duration", "Passed", "ok", [sessionId], [])],
            []);

    private sealed class StubRunReportService(PaperRunReport? report) : IPaperRunReportService
    {
        public Task<PaperRunReport?> GetAsync(
            Guid sessionId,
            int limit = 1000,
            CancellationToken cancellationToken = default)
            => Task.FromResult(report?.Session.SessionId == sessionId ? report : null);
    }

    private sealed class StubPromotionChecklistService(PaperPromotionChecklist? checklist) : IPaperPromotionChecklistService
    {
        public Task<PaperPromotionChecklist?> EvaluateAsync(
            Guid sessionId,
            int limit = 1000,
            CancellationToken cancellationToken = default)
            => Task.FromResult(checklist?.SessionId == sessionId ? checklist : null);
    }

    private sealed class StubRunSessionService(PaperRunSessionRecord? activeSession) : IPaperRunSessionService
    {
        public Task<PaperRunSessionRecord> StartOrRecoverAsync(
            PaperRunSessionStartRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PaperRunSessionRecord?> StopAsync(
            PaperRunSessionStopRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PaperRunSessionRecord?> GetActiveAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(activeSession);

        public Task<PaperRunSessionRecord?> ExportAsync(Guid sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(activeSession?.SessionId == sessionId ? activeSession : null);

        public Task<RunSessionIdentity?> GetCurrentAsync(
            string executionMode,
            CancellationToken cancellationToken = default)
            => Task.FromResult(activeSession is null
                ? null
                : new RunSessionIdentity(
                    activeSession.SessionId,
                    activeSession.ExecutionMode,
                    activeSession.ConfigVersion,
                    activeSession.StartedAtUtc));
    }
}
