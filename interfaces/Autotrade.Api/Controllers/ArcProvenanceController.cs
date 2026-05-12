using Autotrade.ArcSettlement.Application.Contract.Provenance;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Controllers;

[ApiController]
[Route("api/arc/provenance")]
public sealed class ArcProvenanceController(
    IArcStrategyProvenanceService provenanceService) : ControllerBase
{
    [HttpGet("{provenanceHash}")]
    [ProducesResponseType(typeof(ArcSubscriberProvenanceExplanation), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ArcSubscriberProvenanceExplanation>> GetAsync(
        string provenanceHash,
        CancellationToken cancellationToken)
    {
        var explanation = await provenanceService.GetSubscriberExplanationAsync(provenanceHash, cancellationToken)
            .ConfigureAwait(false);
        return explanation is null
            ? NotFound()
            : Ok(explanation);
    }
}
