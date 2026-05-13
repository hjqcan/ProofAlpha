using Autotrade.OpportunityDiscovery.Domain.Exceptions;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using NetDevPack.Domain;

namespace Autotrade.OpportunityDiscovery.Domain.Entities;

public sealed class ExecutableOpportunityPolicy : Entity, IAggregateRoot
{
    private ExecutableOpportunityPolicy()
    {
        PolicyVersion = string.Empty;
        MarketId = string.Empty;
        Outcome = OpportunityOutcomeSide.Yes;
        Status = ExecutableOpportunityPolicyStatus.Draft;
        EvidenceIdsJson = "[]";
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public ExecutableOpportunityPolicy(
        Guid hypothesisId,
        string policyVersion,
        string marketId,
        OpportunityOutcomeSide outcome,
        decimal fairProbability,
        decimal confidence,
        decimal edge,
        decimal entryMaxPrice,
        decimal takeProfitPrice,
        decimal stopLossPrice,
        decimal maxSpread,
        decimal quantity,
        decimal maxNotional,
        DateTimeOffset validFromUtc,
        DateTimeOffset validUntilUtc,
        string evidenceIdsJson,
        DateTimeOffset createdAtUtc)
    {
        HypothesisId = hypothesisId == Guid.Empty
            ? throw new ArgumentException("HypothesisId cannot be empty.", nameof(hypothesisId))
            : hypothesisId;
        PolicyVersion = Required(policyVersion, nameof(policyVersion), 64);
        MarketId = Required(marketId, nameof(marketId), 128);
        Outcome = outcome;
        FairProbability = RequireProbability(fairProbability, nameof(fairProbability));
        Confidence = RequireProbability(confidence, nameof(confidence));
        Edge = edge;
        EntryMaxPrice = RequirePrice(entryMaxPrice, nameof(entryMaxPrice));
        TakeProfitPrice = RequirePrice(takeProfitPrice, nameof(takeProfitPrice));
        StopLossPrice = RequirePrice(stopLossPrice, nameof(stopLossPrice));
        if (StopLossPrice >= EntryMaxPrice || TakeProfitPrice <= EntryMaxPrice)
        {
            throw new ArgumentException("Stop loss must be below entry and take profit must be above entry.");
        }

        MaxSpread = RequireProbability(maxSpread, nameof(maxSpread));
        Quantity = quantity > 0m
            ? quantity
            : throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Quantity must be positive.");
        MaxNotional = maxNotional > 0m
            ? maxNotional
            : throw new ArgumentOutOfRangeException(nameof(maxNotional), maxNotional, "MaxNotional must be positive.");
        ValidFromUtc = validFromUtc == default ? DateTimeOffset.UtcNow : validFromUtc;
        ValidUntilUtc = validUntilUtc > ValidFromUtc
            ? validUntilUtc
            : throw new ArgumentOutOfRangeException(nameof(validUntilUtc), validUntilUtc, "ValidUntilUtc must be after ValidFromUtc.");
        EvidenceIdsJson = string.IsNullOrWhiteSpace(evidenceIdsJson) ? "[]" : evidenceIdsJson.Trim();
        Status = ExecutableOpportunityPolicyStatus.Draft;
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid HypothesisId { get; private set; }

    public string PolicyVersion { get; private set; }

    public string MarketId { get; private set; }

    public OpportunityOutcomeSide Outcome { get; private set; }

    public decimal FairProbability { get; private set; }

    public decimal Confidence { get; private set; }

    public decimal Edge { get; private set; }

    public decimal EntryMaxPrice { get; private set; }

    public decimal TakeProfitPrice { get; private set; }

    public decimal StopLossPrice { get; private set; }

    public decimal MaxSpread { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal MaxNotional { get; private set; }

    public DateTimeOffset ValidFromUtc { get; private set; }

    public DateTimeOffset ValidUntilUtc { get; private set; }

    public string EvidenceIdsJson { get; private set; }

    public ExecutableOpportunityPolicyStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public bool IsExecutableAt(DateTimeOffset now)
        => Status == ExecutableOpportunityPolicyStatus.Active &&
           ValidFromUtc <= now &&
           ValidUntilUtc > now;

    public void Activate(DateTimeOffset now)
    {
        var resolvedNow = now == default ? DateTimeOffset.UtcNow : now;
        if (Status != ExecutableOpportunityPolicyStatus.Draft)
        {
            throw new OpportunityLifecycleException(
                "OpportunityPolicy.InvalidTransition",
                $"Policy {Id} cannot activate from {Status}.");
        }

        if (ValidUntilUtc <= resolvedNow)
        {
            throw new OpportunityLifecycleException(
                "OpportunityPolicy.Expired",
                $"Policy {Id} cannot activate after validUntilUtc.");
        }

        Status = ExecutableOpportunityPolicyStatus.Active;
        UpdatedAtUtc = resolvedNow;
    }

    public void Suspend(DateTimeOffset now)
    {
        if (Status is ExecutableOpportunityPolicyStatus.Suspended or ExecutableOpportunityPolicyStatus.Expired)
        {
            return;
        }

        Status = ExecutableOpportunityPolicyStatus.Suspended;
        UpdatedAtUtc = now == default ? DateTimeOffset.UtcNow : now;
    }

    public bool TryExpire(DateTimeOffset now)
    {
        var resolvedNow = now == default ? DateTimeOffset.UtcNow : now;
        if (Status == ExecutableOpportunityPolicyStatus.Expired || ValidUntilUtc > resolvedNow)
        {
            return false;
        }

        Status = ExecutableOpportunityPolicyStatus.Expired;
        UpdatedAtUtc = resolvedNow;
        return true;
    }

    private static decimal RequirePrice(decimal value, string paramName)
    {
        if (value < 0.01m || value > 0.99m)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Price must be in 0.01..0.99.");
        }

        return value;
    }

    private static decimal RequireProbability(decimal value, string paramName)
    {
        if (value < 0m || value > 1m)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be in 0..1.");
        }

        return value;
    }

    private static string Required(string value, string paramName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{paramName} cannot be empty.", paramName);
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
