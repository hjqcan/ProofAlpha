using Autotrade.ArcSettlement.Application.Contract.Performance;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Controllers;

[ApiController]
[Route("api/arc/performance")]
public sealed class ArcPerformanceController(
    IArcPerformanceRecorder performanceRecorder) : ControllerBase
{
    [HttpGet("agent")]
    [ProducesResponseType(typeof(ArcAgentReputation), StatusCodes.Status200OK)]
    public async Task<ActionResult<ArcAgentReputation>> GetAgentAsync(
        CancellationToken cancellationToken)
    {
        var reputation = await performanceRecorder.GetAgentReputationAsync(cancellationToken)
            .ConfigureAwait(false);
        return Ok(reputation);
    }

    [HttpGet("strategies/{strategyId}")]
    [ProducesResponseType(typeof(ArcAgentReputation), StatusCodes.Status200OK)]
    public async Task<ActionResult<ArcAgentReputation>> GetStrategyAsync(
        string strategyId,
        CancellationToken cancellationToken)
    {
        var reputation = await performanceRecorder.GetStrategyReputationAsync(strategyId, cancellationToken)
            .ConfigureAwait(false);
        return Ok(reputation);
    }

    [HttpGet("signals/{signalId}/outcome")]
    [ProducesResponseType(typeof(ArcPerformanceOutcomeRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ArcPerformanceOutcomeRecord>> GetOutcomeAsync(
        string signalId,
        CancellationToken cancellationToken)
    {
        var outcome = await performanceRecorder.GetOutcomeAsync(signalId, cancellationToken)
            .ConfigureAwait(false);
        return outcome is null
            ? NotFound()
            : Ok(outcome);
    }
}
