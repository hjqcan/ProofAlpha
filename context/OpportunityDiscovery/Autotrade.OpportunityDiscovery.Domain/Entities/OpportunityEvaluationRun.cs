using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using NetDevPack.Domain;

namespace Autotrade.OpportunityDiscovery.Domain.Entities;

public sealed class OpportunityEvaluationRun : Entity, IAggregateRoot
{
    private OpportunityEvaluationRun()
    {
        RunVersion = string.Empty;
        MarketTapeSliceId = string.Empty;
        ReplaySeed = string.Empty;
        ResultJson = "{}";
        StartedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public OpportunityEvaluationRun(
        Guid hypothesisId,
        OpportunityEvaluationKind evaluationKind,
        string runVersion,
        string marketTapeSliceId,
        string replaySeed,
        DateTimeOffset startedAtUtc)
    {
        HypothesisId = hypothesisId == Guid.Empty
            ? throw new ArgumentException("HypothesisId cannot be empty.", nameof(hypothesisId))
            : hypothesisId;
        EvaluationKind = evaluationKind;
        RunVersion = Required(runVersion, nameof(runVersion), 64);
        MarketTapeSliceId = Required(marketTapeSliceId, nameof(marketTapeSliceId), 256);
        ReplaySeed = Required(replaySeed, nameof(replaySeed), 128);
        Status = OpportunityEvaluationRunStatus.Running;
        ResultJson = "{}";
        StartedAtUtc = startedAtUtc == default ? DateTimeOffset.UtcNow : startedAtUtc;
        UpdatedAtUtc = StartedAtUtc;
    }

    public Guid HypothesisId { get; private set; }

    public OpportunityEvaluationKind EvaluationKind { get; private set; }

    public OpportunityEvaluationRunStatus Status { get; private set; }

    public string RunVersion { get; private set; }

    public string MarketTapeSliceId { get; private set; }

    public string ReplaySeed { get; private set; }

    public string ResultJson { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void MarkSucceeded(string resultJson, DateTimeOffset completedAtUtc)
    {
        if (Status != OpportunityEvaluationRunStatus.Running)
        {
            throw new InvalidOperationException($"Evaluation run {Id} cannot complete from {Status}.");
        }

        Status = OpportunityEvaluationRunStatus.Succeeded;
        ResultJson = string.IsNullOrWhiteSpace(resultJson) ? "{}" : resultJson.Trim();
        CompletedAtUtc = completedAtUtc == default ? DateTimeOffset.UtcNow : completedAtUtc;
        UpdatedAtUtc = CompletedAtUtc.Value;
        ErrorMessage = null;
    }

    public void MarkFailed(string errorMessage, DateTimeOffset completedAtUtc)
    {
        if (Status != OpportunityEvaluationRunStatus.Running)
        {
            throw new InvalidOperationException($"Evaluation run {Id} cannot fail from {Status}.");
        }

        Status = OpportunityEvaluationRunStatus.Failed;
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? "Unknown error" : errorMessage.Trim();
        CompletedAtUtc = completedAtUtc == default ? DateTimeOffset.UtcNow : completedAtUtc;
        UpdatedAtUtc = CompletedAtUtc.Value;
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
