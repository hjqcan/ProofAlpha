using Autotrade.OpportunityDiscovery.Application.Contract;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Controllers;

[ApiController]
[Route("api/opportunities")]
public sealed class OpportunityController(
    IOpportunityOperatorService operatorService,
    IOpportunityDiscoveryService discoveryService) : ControllerBase
{
    [HttpPost("user-messages")]
    public async Task<ActionResult<OpportunityUserMessageIngestionResult>> IngestUserMessage(
        [FromBody] OpportunityUserMessageIngestionRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest("Request body is required.");
        }

        var response = await discoveryService.IngestUserMessageAsync(request, cancellationToken).ConfigureAwait(false);
        return Accepted(response);
    }

    [HttpPost("account-activity")]
    public async Task<ActionResult<OpportunityAccountActivityIngestionResult>> IngestAccountActivity(
        [FromBody] OpportunityAccountActivityIngestionRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest("Request body is required.");
        }

        var response = await discoveryService.IngestPolymarketAccountActivityAsync(request, cancellationToken).ConfigureAwait(false);
        return Accepted(response);
    }

    [HttpGet("{opportunityId:guid}/score")]
    public async Task<ActionResult<OpportunityScoreStatusResponse>> GetScore(
        Guid opportunityId,
        CancellationToken cancellationToken)
    {
        var response = await operatorService.GetScoreAsync(opportunityId, cancellationToken).ConfigureAwait(false);
        return IsNotFound(response.BlockingReasons) ? NotFound(response) : Ok(response);
    }

    [HttpGet("{opportunityId:guid}/replay")]
    public async Task<ActionResult<OpportunityReplayStatusResponse>> GetReplay(
        Guid opportunityId,
        CancellationToken cancellationToken)
    {
        var response = await operatorService.GetReplayAsync(opportunityId, cancellationToken).ConfigureAwait(false);
        return IsNotFound(response.BlockingReasons) ? NotFound(response) : Ok(response);
    }

    [HttpPost("{opportunityId:guid}/promote")]
    public async Task<ActionResult<OpportunityPromoteResponse>> Promote(
        Guid opportunityId,
        [FromBody] OpportunityOperatorActionRequest? request,
        CancellationToken cancellationToken)
    {
        var response = await operatorService.PromoteAsync(
                new OpportunityPromoteRequest(
                    opportunityId,
                    request?.Actor ?? "api",
                    request?.Reason ?? "operator promotion"),
                cancellationToken)
            .ConfigureAwait(false);

        if (IsNotFound(response.BlockingReasons))
        {
            return NotFound(response);
        }

        return response.Accepted ? Accepted(response) : Conflict(response);
    }

    [HttpGet("live-status")]
    public async Task<ActionResult<OpportunityLiveStatusResponse>> GetLiveStatus(CancellationToken cancellationToken)
    {
        return Ok(await operatorService.GetLiveStatusAsync(cancellationToken).ConfigureAwait(false));
    }

    [HttpPost("{opportunityId:guid}/suspend")]
    public async Task<ActionResult<OpportunityOperatorSuspendResponse>> Suspend(
        Guid opportunityId,
        [FromBody] OpportunityOperatorActionRequest? request,
        CancellationToken cancellationToken)
    {
        var response = await operatorService.SuspendAsync(
                new OpportunityOperatorSuspendRequest(
                    opportunityId,
                    request?.Actor ?? "api",
                    request?.Reason ?? "operator suspension",
                    request?.StrategyId,
                    request?.MarketId),
                cancellationToken)
            .ConfigureAwait(false);

        if (IsNotFound(response.Reasons))
        {
            return NotFound(response);
        }

        return response.Suspended ? Accepted(response) : Conflict(response);
    }

    [HttpGet("{opportunityId:guid}/explain")]
    public async Task<ActionResult<OpportunityExplainResponse>> Explain(
        Guid opportunityId,
        [FromQuery] DateTimeOffset? asOfUtc,
        CancellationToken cancellationToken)
    {
        var response = await operatorService.ExplainAsync(opportunityId, asOfUtc, cancellationToken).ConfigureAwait(false);
        return IsNotFound(response.BlockingReasons) ? NotFound(response) : Ok(response);
    }

    private static bool IsNotFound(IReadOnlyList<string> reasons)
        => reasons.Any(reason => reason.Contains("not found", StringComparison.OrdinalIgnoreCase));
}
