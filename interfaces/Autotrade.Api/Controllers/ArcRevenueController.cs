using Autotrade.ArcSettlement.Application.Contract.Revenue;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Controllers;

[ApiController]
[Route("api/arc/revenue")]
public sealed class ArcRevenueController(
    IArcRevenueSettlementRecorder revenueRecorder) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ArcRevenueSettlementRecord>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ArcRevenueSettlementRecord>>> ListAsync(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var records = await revenueRecorder.ListAsync(limit, cancellationToken)
            .ConfigureAwait(false);
        return Ok(records);
    }

    [HttpGet("{settlementId}")]
    [ProducesResponseType(typeof(ArcRevenueSettlementRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ArcRevenueSettlementRecord>> GetAsync(
        string settlementId,
        CancellationToken cancellationToken = default)
    {
        var record = await revenueRecorder.GetAsync(settlementId, cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? NotFound()
            : Ok(record);
    }

    [HttpPost("record")]
    [ProducesResponseType(typeof(ArcRevenueSettlementResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ArcRevenueSettlementResult>> RecordAsync(
        [FromBody] ArcRevenueSettlementRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await revenueRecorder.RecordAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return Ok(result);
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            return BadRequest(new
            {
                ErrorCode = "INVALID_REVENUE_SETTLEMENT",
                ex.Message
            });
        }
    }
}
