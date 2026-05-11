using Autotrade.SelfImprove.Application.Contract;
using Autotrade.SelfImprove.Application.Contract.Episodes;
using Autotrade.SelfImprove.Domain.Shared.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Controllers;

[ApiController]
[Route("api/self-improve")]
public sealed class SelfImproveController : ControllerBase
{
    private readonly ISelfImproveService _selfImproveService;

    public SelfImproveController(ISelfImproveService selfImproveService)
    {
        _selfImproveService = selfImproveService ?? throw new ArgumentNullException(nameof(selfImproveService));
    }

    [HttpGet("runs")]
    public async Task<ActionResult<IReadOnlyList<ImprovementRunDto>>> ListRuns(
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _selfImproveService.ListRunsAsync(limit, cancellationToken).ConfigureAwait(false));
    }

    [HttpGet("runs/{runId:guid}")]
    public async Task<ActionResult<SelfImproveRunResult>> GetRun(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var run = await _selfImproveService.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);
        return run is null ? NotFound() : Ok(run);
    }

    [HttpPost("runs")]
    public async Task<ActionResult<SelfImproveRunResult>> Run(
        [FromBody] BuildStrategyEpisodeRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _selfImproveService.RunAsync(request, "api", cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    [HttpPost("proposals/{proposalId:guid}/apply")]
    public async Task<ActionResult<PatchOutcomeDto>> Apply(
        Guid proposalId,
        [FromQuery] bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _selfImproveService.ApplyProposalAsync(
            new ApplyProposalRequest(proposalId, dryRun, "api"),
            cancellationToken).ConfigureAwait(false));
    }

    [HttpPost("generated/{generatedVersionId:guid}/promote")]
    public async Task<ActionResult> Promote(
        Guid generatedVersionId,
        [FromQuery] GeneratedStrategyStage stage,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _selfImproveService.PromoteGeneratedStrategyAsync(generatedVersionId, stage, cancellationToken)
            .ConfigureAwait(false));
    }

    [HttpPost("generated/{generatedVersionId:guid}/rollback")]
    public async Task<ActionResult> Rollback(
        Guid generatedVersionId,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _selfImproveService.RollbackGeneratedStrategyAsync(generatedVersionId, cancellationToken)
            .ConfigureAwait(false));
    }

    [HttpPost("generated/{generatedVersionId:guid}/quarantine")]
    public async Task<ActionResult> Quarantine(
        Guid generatedVersionId,
        [FromBody] QuarantineGeneratedStrategyRequest request,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _selfImproveService.QuarantineGeneratedStrategyAsync(
            generatedVersionId,
            request.Reason,
            cancellationToken).ConfigureAwait(false));
    }
}

public sealed record QuarantineGeneratedStrategyRequest(string Reason);
