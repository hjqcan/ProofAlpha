using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using NetDevPack.Domain;

namespace Autotrade.OpportunityDiscovery.Domain.Entities;

public sealed class MarketOpportunity : Entity, IAggregateRoot
{
    private MarketOpportunity()
    {
        MarketId = string.Empty;
        Outcome = OpportunityOutcomeSide.Yes;
        Status = OpportunityStatus.Candidate;
        Reason = string.Empty;
        EvidenceIdsJson = "[]";
        LlmOutputJson = "{}";
        ScoreJson = "{}";
        CompiledPolicyJson = "{}";
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public MarketOpportunity(
        Guid researchRunId,
        string marketId,
        OpportunityOutcomeSide outcome,
        decimal fairProbability,
        decimal confidence,
        decimal edge,
        DateTimeOffset validUntilUtc,
        string reason,
        string evidenceIdsJson,
        string llmOutputJson,
        string scoreJson,
        string compiledPolicyJson,
        OpportunityStatus initialStatus,
        DateTimeOffset now)
    {
        ResearchRunId = researchRunId == Guid.Empty
            ? throw new ArgumentException("ResearchRunId cannot be empty.", nameof(researchRunId))
            : researchRunId;
        MarketId = string.IsNullOrWhiteSpace(marketId)
            ? throw new ArgumentException("MarketId cannot be empty.", nameof(marketId))
            : marketId.Trim();
        Outcome = outcome;
        FairProbability = RequireProbability(fairProbability, nameof(fairProbability));
        Confidence = RequireProbability(confidence, nameof(confidence));
        Edge = edge;
        ValidUntilUtc = validUntilUtc == default
            ? throw new ArgumentException("ValidUntilUtc cannot be default.", nameof(validUntilUtc))
            : validUntilUtc;
        Reason = string.IsNullOrWhiteSpace(reason) ? "No reason supplied." : reason.Trim();
        EvidenceIdsJson = string.IsNullOrWhiteSpace(evidenceIdsJson) ? "[]" : evidenceIdsJson.Trim();
        LlmOutputJson = string.IsNullOrWhiteSpace(llmOutputJson) ? "{}" : llmOutputJson.Trim();
        ScoreJson = string.IsNullOrWhiteSpace(scoreJson) ? "{}" : scoreJson.Trim();
        CompiledPolicyJson = string.IsNullOrWhiteSpace(compiledPolicyJson) ? "{}" : compiledPolicyJson.Trim();
        Status = initialStatus is OpportunityStatus.Candidate or OpportunityStatus.NeedsReview
            ? initialStatus
            : throw new ArgumentOutOfRangeException(nameof(initialStatus), initialStatus, "Initial status must be Candidate or NeedsReview.");
        CreatedAtUtc = now == default ? DateTimeOffset.UtcNow : now;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid ResearchRunId { get; private set; }

    public string MarketId { get; private set; }

    public OpportunityOutcomeSide Outcome { get; private set; }

    public decimal FairProbability { get; private set; }

    public decimal Confidence { get; private set; }

    public decimal Edge { get; private set; }

    public OpportunityStatus Status { get; private set; }

    public DateTimeOffset ValidUntilUtc { get; private set; }

    public string Reason { get; private set; }

    public string EvidenceIdsJson { get; private set; }

    public string LlmOutputJson { get; private set; }

    public string ScoreJson { get; private set; }

    public string CompiledPolicyJson { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Approve(DateTimeOffset now)
    {
        if (Status is not OpportunityStatus.Candidate)
        {
            throw new InvalidOperationException("Only candidate opportunities can be approved.");
        }

        if (ValidUntilUtc <= (now == default ? DateTimeOffset.UtcNow : now))
        {
            throw new InvalidOperationException("Expired opportunities cannot be approved.");
        }

        Status = OpportunityStatus.Approved;
        UpdatedAtUtc = now == default ? DateTimeOffset.UtcNow : now;
    }

    public void Reject(DateTimeOffset now)
    {
        if (Status is OpportunityStatus.Published)
        {
            throw new InvalidOperationException("Published opportunities cannot be rejected.");
        }

        Status = OpportunityStatus.Rejected;
        UpdatedAtUtc = now == default ? DateTimeOffset.UtcNow : now;
    }

    public void Publish(DateTimeOffset now)
    {
        var resolvedNow = now == default ? DateTimeOffset.UtcNow : now;
        if (Status is not OpportunityStatus.Approved)
        {
            throw new InvalidOperationException("Only approved opportunities can be published.");
        }

        if (ValidUntilUtc <= resolvedNow)
        {
            throw new InvalidOperationException("Expired opportunities cannot be published.");
        }

        Status = OpportunityStatus.Published;
        UpdatedAtUtc = resolvedNow;
    }

    public void ReplaceCompiledPolicyJson(string compiledPolicyJson, DateTimeOffset now)
    {
        CompiledPolicyJson = string.IsNullOrWhiteSpace(compiledPolicyJson)
            ? throw new ArgumentException("CompiledPolicyJson cannot be empty.", nameof(compiledPolicyJson))
            : compiledPolicyJson.Trim();
        UpdatedAtUtc = now == default ? DateTimeOffset.UtcNow : now;
    }

    public bool TryExpire(DateTimeOffset now)
    {
        var resolvedNow = now == default ? DateTimeOffset.UtcNow : now;
        if (Status is OpportunityStatus.Expired or OpportunityStatus.Rejected || ValidUntilUtc > resolvedNow)
        {
            return false;
        }

        Status = OpportunityStatus.Expired;
        UpdatedAtUtc = resolvedNow;
        return true;
    }

    private static decimal RequireProbability(decimal value, string paramName)
    {
        if (value < 0m || value > 1m)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Probability must be in 0..1.");
        }

        return value;
    }
}
