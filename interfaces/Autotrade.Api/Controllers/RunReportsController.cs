using Autotrade.Strategy.Application.RunReports;
using Autotrade.Strategy.Application.Promotion;
using Autotrade.Strategy.Application.RunSessions;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Controllers;

[ApiController]
[Route("api/run-reports")]
public sealed class RunReportsController(
    IPaperRunReportService reportService,
    IPaperPromotionChecklistService checklistService,
    IPaperRunSessionService sessionService) : ControllerBase
{
    [HttpGet("active")]
    public async Task<ActionResult<PaperRunSessionRecord?>> GetActive(CancellationToken cancellationToken)
    {
        var session = await sessionService.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        return Ok(session);
    }

    [HttpGet("{sessionId:guid}")]
    public async Task<ActionResult<PaperRunReport>> Get(
        Guid sessionId,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var report = await reportService
            .GetAsync(sessionId, Math.Clamp(limit ?? 1000, 1, 5000), cancellationToken)
            .ConfigureAwait(false);

        return report is null ? NotFound() : Ok(report);
    }

    [HttpGet("{sessionId:guid}/promotion-checklist")]
    public async Task<ActionResult<PaperPromotionChecklist>> GetPromotionChecklist(
        Guid sessionId,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var checklist = await checklistService
            .EvaluateAsync(sessionId, Math.Clamp(limit ?? 1000, 1, 5000), cancellationToken)
            .ConfigureAwait(false);

        return checklist is null ? NotFound() : Ok(checklist);
    }
}
