using Autotrade.ArcSettlement.Application.Contract.Access;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Controllers;

[ApiController]
[Route("api/arc/access")]
public sealed class ArcAccessController(
    IArcSubscriptionPlanService planService,
    IArcStrategyAccessReader accessReader,
    IArcSubscriptionSyncService syncService) : ControllerBase
{
    [HttpGet("plans")]
    public ActionResult<IReadOnlyList<ArcSubscriptionPlan>> Plans()
        => Ok(planService.ListPlans());

    [HttpGet("{walletAddress}/{strategyKey}")]
    public async Task<ActionResult<ArcStrategyAccessStatus>> Get(
        string walletAddress,
        string strategyKey,
        CancellationToken cancellationToken = default)
    {
        var status = await accessReader.GetAccessAsync(walletAddress, strategyKey, cancellationToken)
            .ConfigureAwait(false);
        return Ok(status);
    }

    [HttpPost("sync")]
    public async Task<ActionResult<ArcSubscriptionSyncResult>> Sync(
        [FromBody] SyncArcAccessRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await syncService.SyncAsync(request, cancellationToken).ConfigureAwait(false);
            return Ok(result);
        }
        catch (ArcSubscriptionSyncException ex) when (IsNotFound(ex))
        {
            return NotFound(new { ex.ErrorCode, ex.Message });
        }
        catch (ArcSubscriptionSyncException ex)
        {
            return BadRequest(new { ex.ErrorCode, ex.Message });
        }
    }

    private static bool IsNotFound(ArcSubscriptionSyncException exception)
        => string.Equals(exception.ErrorCode, "PLAN_NOT_FOUND", StringComparison.OrdinalIgnoreCase);
}
