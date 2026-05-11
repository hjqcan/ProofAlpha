using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using NetDevPack.Domain;

namespace Autotrade.OpportunityDiscovery.Domain.Entities;

public sealed class ResearchRun : Entity, IAggregateRoot
{
    private ResearchRun()
    {
        Trigger = string.Empty;
        MarketUniverseJson = "[]";
        Status = ResearchRunStatus.Pending;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public ResearchRun(string trigger, string marketUniverseJson, DateTimeOffset createdAtUtc)
    {
        Trigger = string.IsNullOrWhiteSpace(trigger)
            ? throw new ArgumentException("Trigger cannot be empty.", nameof(trigger))
            : trigger.Trim();
        MarketUniverseJson = string.IsNullOrWhiteSpace(marketUniverseJson) ? "[]" : marketUniverseJson.Trim();
        Status = ResearchRunStatus.Pending;
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public string Trigger { get; private set; }

    public string MarketUniverseJson { get; private set; }

    public ResearchRunStatus Status { get; private set; }

    public int EvidenceCount { get; private set; }

    public int OpportunityCount { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void MarkRunning(DateTimeOffset now)
    {
        if (Status is not ResearchRunStatus.Pending)
        {
            throw new InvalidOperationException($"Run {Id} cannot move from {Status} to Running.");
        }

        Status = ResearchRunStatus.Running;
        UpdatedAtUtc = now == default ? DateTimeOffset.UtcNow : now;
    }

    public void MarkSucceeded(int evidenceCount, int opportunityCount, DateTimeOffset now)
    {
        if (Status is not ResearchRunStatus.Running and not ResearchRunStatus.Pending)
        {
            throw new InvalidOperationException($"Run {Id} cannot move from {Status} to Succeeded.");
        }

        EvidenceCount = Math.Max(0, evidenceCount);
        OpportunityCount = Math.Max(0, opportunityCount);
        Status = ResearchRunStatus.Succeeded;
        ErrorMessage = null;
        UpdatedAtUtc = now == default ? DateTimeOffset.UtcNow : now;
    }

    public void MarkFailed(string errorMessage, DateTimeOffset now)
    {
        Status = ResearchRunStatus.Failed;
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? "Unknown error" : errorMessage.Trim();
        UpdatedAtUtc = now == default ? DateTimeOffset.UtcNow : now;
    }
}
