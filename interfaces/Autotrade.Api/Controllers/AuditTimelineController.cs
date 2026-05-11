using Autotrade.Strategy.Application.Audit;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Controllers;

[ApiController]
[Route("api/audit-timeline")]
public sealed class AuditTimelineController(IAuditTimelineService timelineService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AuditTimeline>> Query(
        [FromQuery] string? strategyId,
        [FromQuery] string? marketId,
        [FromQuery] Guid? orderId,
        [FromQuery] string? clientOrderId,
        [FromQuery] Guid? runSessionId,
        [FromQuery] Guid? riskEventId,
        [FromQuery] string? correlationId,
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var timeline = await timelineService
            .QueryAsync(
                new AuditTimelineQuery(
                    strategyId,
                    marketId,
                    orderId,
                    clientOrderId,
                    runSessionId,
                    riskEventId,
                    correlationId,
                    fromUtc,
                    toUtc,
                    limit ?? 200),
                cancellationToken)
            .ConfigureAwait(false);

        return Ok(timeline);
    }
}
