using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using NetDevPack.Domain;

namespace Autotrade.OpportunityDiscovery.Domain.Entities;

public sealed class OpportunityLiveAllocation : Entity, IAggregateRoot
{
    private OpportunityLiveAllocation()
    {
        Status = OpportunityLiveAllocationStatus.Active;
        Reason = string.Empty;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public OpportunityLiveAllocation(
        Guid hypothesisId,
        Guid executablePolicyId,
        decimal maxNotional,
        decimal maxContracts,
        DateTimeOffset validUntilUtc,
        string reason,
        DateTimeOffset createdAtUtc)
    {
        HypothesisId = hypothesisId == Guid.Empty
            ? throw new ArgumentException("HypothesisId cannot be empty.", nameof(hypothesisId))
            : hypothesisId;
        ExecutablePolicyId = executablePolicyId == Guid.Empty
            ? throw new ArgumentException("ExecutablePolicyId cannot be empty.", nameof(executablePolicyId))
            : executablePolicyId;
        MaxNotional = maxNotional > 0m
            ? maxNotional
            : throw new ArgumentOutOfRangeException(nameof(maxNotional), maxNotional, "MaxNotional must be positive.");
        MaxContracts = maxContracts > 0m
            ? maxContracts
            : throw new ArgumentOutOfRangeException(nameof(maxContracts), maxContracts, "MaxContracts must be positive.");
        ValidUntilUtc = validUntilUtc == default
            ? throw new ArgumentException("ValidUntilUtc cannot be default.", nameof(validUntilUtc))
            : validUntilUtc;
        Reason = Required(reason, nameof(reason), 2048);
        Status = OpportunityLiveAllocationStatus.Active;
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid HypothesisId { get; private set; }

    public Guid ExecutablePolicyId { get; private set; }

    public decimal MaxNotional { get; private set; }

    public decimal MaxContracts { get; private set; }

    public DateTimeOffset ValidUntilUtc { get; private set; }

    public OpportunityLiveAllocationStatus Status { get; private set; }

    public string Reason { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public bool IsActiveAt(DateTimeOffset now)
        => Status == OpportunityLiveAllocationStatus.Active && ValidUntilUtc > now;

    public void Suspend(DateTimeOffset now)
        => Suspend(Reason, now);

    public void Suspend(string reason, DateTimeOffset now)
    {
        if (Status != OpportunityLiveAllocationStatus.Active)
        {
            return;
        }

        Status = OpportunityLiveAllocationStatus.Suspended;
        Reason = Required(reason, nameof(reason), 2048);
        UpdatedAtUtc = now == default ? DateTimeOffset.UtcNow : now;
    }

    public bool TryExpire(DateTimeOffset now)
    {
        var resolvedNow = now == default ? DateTimeOffset.UtcNow : now;
        if (Status == OpportunityLiveAllocationStatus.Expired || ValidUntilUtc > resolvedNow)
        {
            return false;
        }

        Status = OpportunityLiveAllocationStatus.Expired;
        UpdatedAtUtc = resolvedNow;
        return true;
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
