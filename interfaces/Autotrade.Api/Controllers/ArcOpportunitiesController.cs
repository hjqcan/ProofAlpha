using Autotrade.ArcSettlement.Application.Contract.Access;
using Autotrade.ArcSettlement.Application.Contract.Configuration;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Autotrade.Api.Controllers;

[ApiController]
[Route("api/arc/opportunities")]
public sealed class ArcOpportunitiesController(
    IOpportunityQueryService opportunities,
    IArcAccessDecisionService accessDecisionService,
    IOptionsMonitor<ArcSettlementOptions> options) : ControllerBase
{
    private const string WalletHeaderName = "X-Arc-Wallet";

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ArcOpportunitySummaryResponse>>> List(
        [FromQuery] string? status = null,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseStatus(status, out var parsedStatus))
        {
            return BadRequest("status must be a valid OpportunityStatus value.");
        }

        var records = await opportunities
            .ListOpportunitiesAsync(parsedStatus, limit, cancellationToken)
            .ConfigureAwait(false);

        return Ok(records.Select(ToSummary).ToArray());
    }

    [HttpGet("{opportunityId:guid}")]
    public async Task<ActionResult<ArcOpportunityDetailResponse>> Get(
        Guid opportunityId,
        [FromQuery] string? walletAddress = null,
        CancellationToken cancellationToken = default)
    {
        var opportunity = await opportunities.GetOpportunityAsync(opportunityId, cancellationToken)
            .ConfigureAwait(false);
        if (opportunity is null)
        {
            return NotFound();
        }

        var strategyKey = ResolveOpportunityStrategyKey();
        var wallet = ResolveWalletAddress(walletAddress);
        var signalDecision = await accessDecisionService
            .EvaluateAsync(
                new ArcAccessDecisionRequest(
                    wallet,
                    strategyKey,
                    ArcEntitlementPermission.ViewSignals,
                    "arc-opportunity",
                    opportunity.Id.ToString("D")),
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
                    strategyKey,
                    ArcEntitlementPermission.ViewReasoning,
                    "arc-opportunity-reasoning",
                    opportunity.Id.ToString("D")),
                cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<EvidenceItemDto> evidence = reasoningDecision.Allowed
            ? await opportunities.GetEvidenceAsync(opportunity.Id, cancellationToken).ConfigureAwait(false)
            : [];

        return Ok(ToDetail(opportunity, signalDecision, reasoningDecision, evidence));
    }

    private string ResolveOpportunityStrategyKey()
    {
        var configured = options.CurrentValue.SignalProof.OpportunityStrategyId;
        return string.IsNullOrWhiteSpace(configured)
            ? "llm_opportunity"
            : configured.Trim();
    }

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

    private static bool TryParseStatus(string? status, out OpportunityStatus? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(status))
        {
            return true;
        }

        if (Enum.TryParse<OpportunityStatus>(status.Trim(), ignoreCase: true, out var value))
        {
            parsed = value;
            return true;
        }

        return false;
    }

    private static ArcOpportunitySummaryResponse ToSummary(MarketOpportunityDto opportunity)
        => new(
            opportunity.Id,
            opportunity.MarketId,
            opportunity.Outcome.ToString(),
            opportunity.Edge,
            opportunity.Status.ToString(),
            opportunity.ValidUntilUtc,
            opportunity.CreatedAtUtc,
            opportunity.UpdatedAtUtc);

    private static ArcOpportunityDetailResponse ToDetail(
        MarketOpportunityDto opportunity,
        ArcAccessDecision signalDecision,
        ArcAccessDecision reasoningDecision,
        IReadOnlyList<EvidenceItemDto> evidence)
        => new(
            opportunity.Id,
            opportunity.ResearchRunId,
            opportunity.MarketId,
            opportunity.Outcome.ToString(),
            opportunity.FairProbability,
            opportunity.Confidence,
            opportunity.Edge,
            opportunity.Status.ToString(),
            opportunity.ValidUntilUtc,
            opportunity.CompiledPolicyJson,
            reasoningDecision.Allowed ? opportunity.Reason : null,
            reasoningDecision.Allowed ? opportunity.ScoreJson : null,
            evidence,
            signalDecision,
            reasoningDecision,
            opportunity.CreatedAtUtc,
            opportunity.UpdatedAtUtc);
}

public sealed record ArcOpportunitySummaryResponse(
    Guid OpportunityId,
    string MarketId,
    string Outcome,
    decimal Edge,
    string Status,
    DateTimeOffset ValidUntilUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ArcOpportunityDetailResponse(
    Guid OpportunityId,
    Guid ResearchRunId,
    string MarketId,
    string Outcome,
    decimal FairProbability,
    decimal Confidence,
    decimal Edge,
    string Status,
    DateTimeOffset ValidUntilUtc,
    string CompiledPolicyJson,
    string? Reason,
    string? ScoreJson,
    IReadOnlyList<EvidenceItemDto> Evidence,
    ArcAccessDecision SignalDecision,
    ArcAccessDecision ReasoningDecision,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
