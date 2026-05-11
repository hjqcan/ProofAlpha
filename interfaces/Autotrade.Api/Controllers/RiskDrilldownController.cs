using Autotrade.Trading.Application.Contract.Risk;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Controllers;

[ApiController]
[Route("api/control-room/risk")]
public sealed class RiskDrilldownController(IRiskDrilldownService riskDrilldownService) : ControllerBase
{
    [HttpGet("events/{riskEventId:guid}")]
    public async Task<ActionResult<RiskEventDrilldown>> GetRiskEvent(
        Guid riskEventId,
        CancellationToken cancellationToken)
    {
        var drilldown = await riskDrilldownService
            .GetRiskEventAsync(riskEventId, cancellationToken)
            .ConfigureAwait(false);

        return drilldown is null ? NotFound() : Ok(drilldown);
    }

    [HttpGet("events/{riskEventId:guid}/csv")]
    public async Task<IActionResult> ExportRiskEventCsv(
        Guid riskEventId,
        CancellationToken cancellationToken)
    {
        var drilldown = await riskDrilldownService
            .GetRiskEventAsync(riskEventId, cancellationToken)
            .ConfigureAwait(false);

        return drilldown is null
            ? NotFound()
            : Content(RiskDrilldownCsvFormatter.FormatRiskEvents([drilldown]), "text/csv");
    }

    [HttpGet("unhedged-exposures")]
    public async Task<ActionResult<UnhedgedExposureDrilldownResponse>> QueryUnhedgedExposures(
        [FromQuery] string? strategyId,
        [FromQuery] string? marketId,
        [FromQuery] Guid? riskEventId,
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var response = await riskDrilldownService
            .QueryUnhedgedExposuresAsync(
                new RiskDrilldownQuery(strategyId, marketId, riskEventId, fromUtc, toUtc, limit ?? 100),
                cancellationToken)
            .ConfigureAwait(false);

        return Ok(response);
    }

    [HttpGet("unhedged-exposures.csv")]
    public async Task<IActionResult> ExportUnhedgedExposuresCsv(
        [FromQuery] string? strategyId,
        [FromQuery] string? marketId,
        [FromQuery] Guid? riskEventId,
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var response = await riskDrilldownService
            .QueryUnhedgedExposuresAsync(
                new RiskDrilldownQuery(strategyId, marketId, riskEventId, fromUtc, toUtc, limit ?? 100),
                cancellationToken)
            .ConfigureAwait(false);

        return Content(RiskDrilldownCsvFormatter.FormatUnhedgedExposures(response.Exposures), "text/csv");
    }
}
