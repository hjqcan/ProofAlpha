using Autotrade.Api.ControlRoom;
using Autotrade.Strategy.Application.Parameters;
using Autotrade.Trading.Application.Contract.Execution;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Autotrade.Api.Controllers;

[ApiController]
[Route("api/strategy-parameters")]
public sealed class StrategyParametersController(
    IStrategyParameterVersionService parameterService,
    IServiceProvider serviceProvider,
    IOptionsMonitor<ControlRoomOptions> controlRoomOptions) : ControllerBase
{
    [HttpGet("{strategyId}")]
    public async Task<ActionResult<StrategyParameterSnapshot>> GetSnapshot(
        string strategyId,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await parameterService
                .GetSnapshotAsync(strategyId, limit ?? 10, cancellationToken)
                .ConfigureAwait(false));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { status = "InvalidRequest", message = exception.Message });
        }
    }

    [HttpPost("{strategyId}/versions")]
    public async Task<ActionResult<StrategyParameterMutationResult>> Update(
        string strategyId,
        [FromBody] StrategyParameterUpdateRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Changes is null)
        {
            return BadRequest(new { status = "InvalidRequest", message = "Changes are required." });
        }

        if (!await EnsureLiveArmingInvalidatedAsync(
                request.Actor,
                request.Reason,
                request.InvalidateLiveArming,
                request.LiveDisarmConfirmationText,
                cancellationToken).ConfigureAwait(false))
        {
            return Conflict(new
            {
                status = "LiveArmingInvalidationRequired",
                message = "Live arming is active. Confirm disarming live trading before changing strategy parameters."
            });
        }

        try
        {
            var result = await parameterService.UpdateAsync(
                    strategyId,
                    new StrategyParameterMutationRequest(
                        request.Changes,
                        request.Actor,
                        "control-room-api",
                        request.Reason),
                    cancellationToken)
                .ConfigureAwait(false);

            return result.Accepted ? Accepted(result) : BadRequest(result);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { status = "InvalidRequest", message = exception.Message });
        }
    }

    [HttpPost("{strategyId}/versions/{versionId:guid}/rollback")]
    public async Task<ActionResult<StrategyParameterMutationResult>> Rollback(
        string strategyId,
        Guid versionId,
        [FromBody] StrategyParameterRollbackApiRequest request,
        CancellationToken cancellationToken)
    {
        if (!await EnsureLiveArmingInvalidatedAsync(
                request.Actor,
                request.Reason,
                request.InvalidateLiveArming,
                request.LiveDisarmConfirmationText,
                cancellationToken).ConfigureAwait(false))
        {
            return Conflict(new
            {
                status = "LiveArmingInvalidationRequired",
                message = "Live arming is active. Confirm disarming live trading before rolling back strategy parameters."
            });
        }

        try
        {
            var result = await parameterService.RollbackAsync(
                    strategyId,
                    new StrategyParameterRollbackRequest(
                        versionId,
                        request.Actor,
                        "control-room-api",
                        request.Reason),
                    cancellationToken)
                .ConfigureAwait(false);

            return result.Accepted ? Accepted(result) : BadRequest(result);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { status = "InvalidRequest", message = exception.Message });
        }
    }

    private async Task<bool> EnsureLiveArmingInvalidatedAsync(
        string? actor,
        string? reason,
        bool invalidateLiveArming,
        string? liveDisarmConfirmationText,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                controlRoomOptions.CurrentValue.EffectiveCommandMode,
                ControlRoomCommandModes.LiveServices,
                StringComparison.Ordinal))
        {
            return true;
        }

        var liveArmingService = serviceProvider.GetService<ILiveArmingService>();
        if (liveArmingService is null)
        {
            return true;
        }

        var status = await liveArmingService.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!status.IsArmed)
        {
            return true;
        }

        if (!invalidateLiveArming)
        {
            return false;
        }

        var result = await liveArmingService
            .DisarmAsync(
                new LiveDisarmingRequest(
                    string.IsNullOrWhiteSpace(actor) ? Environment.UserName : actor.Trim(),
                    string.IsNullOrWhiteSpace(reason)
                        ? "Strategy parameter change invalidated Live arming."
                        : reason.Trim(),
                    liveDisarmConfirmationText),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Accepted;
    }
}

public sealed record StrategyParameterUpdateRequest(
    IReadOnlyDictionary<string, string> Changes,
    string? Actor,
    string? Reason,
    bool InvalidateLiveArming,
    string? LiveDisarmConfirmationText);

public sealed record StrategyParameterRollbackApiRequest(
    string? Actor,
    string? Reason,
    bool InvalidateLiveArming,
    string? LiveDisarmConfirmationText);
