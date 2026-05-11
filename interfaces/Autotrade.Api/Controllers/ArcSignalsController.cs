using Autotrade.ArcSettlement.Application.Contract.Signals;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Controllers;

[ApiController]
[Route("api/arc/signals")]
public sealed class ArcSignalsController(IArcSignalPublicationService publicationService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ArcSignalPublicationRecord>>> List(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var records = await publicationService.ListAsync(
                new ArcSignalPublicationQuery(limit),
                cancellationToken)
            .ConfigureAwait(false);
        return Ok(records);
    }

    [HttpGet("{signalId}")]
    public async Task<ActionResult<ArcSignalPublicationRecord>> Get(
        string signalId,
        CancellationToken cancellationToken = default)
    {
        var record = await publicationService.GetAsync(signalId, cancellationToken).ConfigureAwait(false);
        return record is null ? NotFound() : Ok(record);
    }

    [HttpPost("publish")]
    public async Task<ActionResult<ArcSignalPublicationResult>> Publish(
        [FromBody] PublishArcSignalRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Actor) || string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest("actor and reason are required.");
        }

        var result = await publicationService.PublishAsync(request, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }
}
