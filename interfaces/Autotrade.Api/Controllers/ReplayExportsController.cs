using Autotrade.Strategy.Application.Audit;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Controllers;

[ApiController]
[Route("api/replay-exports")]
public sealed class ReplayExportsController(IReplayExportService replayExportService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ReplayExportPackage>> Export(
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
        var package = await replayExportService
            .ExportAsync(
                new ReplayExportQuery(
                    strategyId,
                    marketId,
                    orderId,
                    clientOrderId,
                    runSessionId,
                    riskEventId,
                    correlationId,
                    fromUtc,
                    toUtc,
                    Math.Clamp(limit ?? 1000, 1, 5000)),
                cancellationToken)
            .ConfigureAwait(false);

        return Ok(package);
    }
}
