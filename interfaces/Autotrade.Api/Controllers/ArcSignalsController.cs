using Autotrade.ArcSettlement.Application.Contract.Access;
using Autotrade.ArcSettlement.Application.Contract.Signals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Controllers;

[ApiController]
[Route("api/arc/signals")]
public sealed class ArcSignalsController(
    IArcSignalPublicationService publicationService,
    IArcAccessDecisionService accessDecisionService) : ControllerBase
{
    private const string WalletHeaderName = "X-Arc-Wallet";

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ArcSignalSummaryResponse>>> List(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var records = await publicationService.ListAsync(
                new ArcSignalPublicationQuery(limit),
                cancellationToken)
            .ConfigureAwait(false);
        return Ok(records.Select(ToSummary).ToArray());
    }

    [HttpGet("{signalId}")]
    public async Task<ActionResult<ArcSignalDetailResponse>> Get(
        string signalId,
        [FromQuery] string? walletAddress = null,
        CancellationToken cancellationToken = default)
    {
        var record = await publicationService.GetAsync(signalId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        var wallet = ResolveWalletAddress(walletAddress);
        var signalDecision = await accessDecisionService
            .EvaluateAsync(
                new ArcAccessDecisionRequest(
                    wallet,
                    record.StrategyId,
                    ArcEntitlementPermission.ViewSignals,
                    "arc-signal",
                    record.SignalId),
                cancellationToken)
            .ConfigureAwait(false);

        if (!signalDecision.Allowed)
        {
            return StatusCode(StatusCodes.Status403Forbidden, signalDecision);
        }

        var reasoningDecision = await accessDecisionService
            .EvaluateAsync(
                new ArcAccessDecisionRequest(
                    wallet,
                    record.StrategyId,
                    ArcEntitlementPermission.ViewReasoning,
                    "arc-signal-reasoning",
                    record.SignalId),
                cancellationToken)
            .ConfigureAwait(false);

        return Ok(ToDetail(record, signalDecision, reasoningDecision));
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

    [HttpPost("publish-source")]
    public async Task<ActionResult<ArcSignalPublicationResult>> PublishSource(
        [FromBody] PublishArcSignalSourceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Actor) || string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest("actor and reason are required.");
        }

        try
        {
            var result = await publicationService.PublishFromSourceAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return Ok(result);
        }
        catch (ArcSignalSourceResolutionException ex) when (IsNotFound(ex))
        {
            return NotFound(ex.Message);
        }
        catch (ArcSignalSourceResolutionException ex)
        {
            return BadRequest(new { ex.ErrorCode, ex.Message });
        }
    }

    private static bool IsNotFound(ArcSignalSourceResolutionException exception)
        => string.Equals(exception.ErrorCode, "SOURCE_NOT_FOUND", StringComparison.OrdinalIgnoreCase);

    private string? ResolveWalletAddress(string? walletAddress)
    {
        if (!string.IsNullOrWhiteSpace(walletAddress))
        {
            return walletAddress;
        }

        return ControllerContext.HttpContext?.Request.Headers.TryGetValue(WalletHeaderName, out var header) == true
            ? header.ToString()
            : null;
    }

    private static ArcSignalSummaryResponse ToSummary(ArcSignalPublicationRecord record)
        => new(
            record.SignalId,
            record.SourceKind.ToString(),
            record.SourceId,
            record.StrategyId,
            record.MarketId,
            record.Venue,
            record.ExpectedEdgeBps,
            record.MaxNotionalUsdc,
            record.ValidUntilUtc,
            record.Status.ToString(),
            record.CreatedAtUtc,
            record.PublishedAtUtc);

    private static ArcSignalDetailResponse ToDetail(
        ArcSignalPublicationRecord record,
        ArcAccessDecision signalDecision,
        ArcAccessDecision reasoningDecision)
        => new(
            record.SignalId,
            record.SourceKind.ToString(),
            record.SourceId,
            record.AgentId,
            record.StrategyId,
            record.MarketId,
            record.Venue,
            reasoningDecision.Allowed ? record.ReasoningHash : null,
            reasoningDecision.Allowed ? record.RiskEnvelopeHash : null,
            record.ExpectedEdgeBps,
            record.MaxNotionalUsdc,
            record.ValidUntilUtc,
            record.Status.ToString(),
            record.SignalHash,
            reasoningDecision.Allowed ? record.SourcePolicyHash : null,
            record.TransactionHash,
            record.ExplorerUrl,
            record.ErrorCode,
            record.CreatedAtUtc,
            record.PublishedAtUtc,
            record.Actor,
            record.Reason,
            signalDecision,
            reasoningDecision);
}

public sealed record ArcSignalSummaryResponse(
    string SignalId,
    string SourceKind,
    string SourceId,
    string StrategyId,
    string MarketId,
    string Venue,
    decimal ExpectedEdgeBps,
    decimal MaxNotionalUsdc,
    DateTimeOffset ValidUntilUtc,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PublishedAtUtc);

public sealed record ArcSignalDetailResponse(
    string SignalId,
    string SourceKind,
    string SourceId,
    string AgentId,
    string StrategyId,
    string MarketId,
    string Venue,
    string? ReasoningHash,
    string? RiskEnvelopeHash,
    decimal ExpectedEdgeBps,
    decimal MaxNotionalUsdc,
    DateTimeOffset ValidUntilUtc,
    string Status,
    string SignalHash,
    string? SourcePolicyHash,
    string? TransactionHash,
    string? ExplorerUrl,
    string? ErrorCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PublishedAtUtc,
    string Actor,
    string Reason,
    ArcAccessDecision SignalDecision,
    ArcAccessDecision ReasoningDecision);
