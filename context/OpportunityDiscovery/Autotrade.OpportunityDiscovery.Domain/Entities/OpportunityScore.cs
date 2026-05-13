using NetDevPack.Domain;

namespace Autotrade.OpportunityDiscovery.Domain.Entities;

public sealed class OpportunityScore : Entity, IAggregateRoot
{
    private OpportunityScore()
    {
        ScoreVersion = string.Empty;
        CalibrationBucket = string.Empty;
        ComponentsJson = "{}";
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public OpportunityScore(
        Guid hypothesisId,
        Guid featureSnapshotId,
        string scoreVersion,
        decimal llmFairProbability,
        decimal fairProbability,
        decimal confidence,
        decimal edge,
        decimal marketImpliedProbability,
        decimal executableEntryPrice,
        decimal feeEstimate,
        decimal slippageBuffer,
        decimal netEdge,
        decimal executableCapacity,
        bool canPromote,
        string calibrationBucket,
        string componentsJson,
        DateTimeOffset createdAtUtc)
    {
        HypothesisId = hypothesisId == Guid.Empty
            ? throw new ArgumentException("HypothesisId cannot be empty.", nameof(hypothesisId))
            : hypothesisId;
        FeatureSnapshotId = featureSnapshotId == Guid.Empty
            ? throw new ArgumentException("FeatureSnapshotId cannot be empty.", nameof(featureSnapshotId))
            : featureSnapshotId;
        ScoreVersion = Required(scoreVersion, nameof(scoreVersion), 64);
        LlmFairProbability = RequireProbability(llmFairProbability, nameof(llmFairProbability));
        FairProbability = RequireProbability(fairProbability, nameof(fairProbability));
        Confidence = RequireProbability(confidence, nameof(confidence));
        Edge = edge;
        MarketImpliedProbability = RequireProbability(marketImpliedProbability, nameof(marketImpliedProbability));
        ExecutableEntryPrice = RequireProbability(executableEntryPrice, nameof(executableEntryPrice));
        FeeEstimate = feeEstimate < 0m
            ? throw new ArgumentOutOfRangeException(nameof(feeEstimate), feeEstimate, "FeeEstimate cannot be negative.")
            : feeEstimate;
        SlippageBuffer = slippageBuffer < 0m
            ? throw new ArgumentOutOfRangeException(nameof(slippageBuffer), slippageBuffer, "SlippageBuffer cannot be negative.")
            : slippageBuffer;
        NetEdge = netEdge;
        ExecutableCapacity = executableCapacity < 0m
            ? throw new ArgumentOutOfRangeException(nameof(executableCapacity), executableCapacity, "ExecutableCapacity cannot be negative.")
            : executableCapacity;
        CanPromote = canPromote;
        CalibrationBucket = Required(calibrationBucket, nameof(calibrationBucket), 128);
        ComponentsJson = string.IsNullOrWhiteSpace(componentsJson) ? "{}" : componentsJson.Trim();
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc;
    }

    public Guid HypothesisId { get; private set; }

    public Guid FeatureSnapshotId { get; private set; }

    public string ScoreVersion { get; private set; }

    public decimal LlmFairProbability { get; private set; }

    public decimal FairProbability { get; private set; }

    public decimal Confidence { get; private set; }

    public decimal Edge { get; private set; }

    public decimal MarketImpliedProbability { get; private set; }

    public decimal ExecutableEntryPrice { get; private set; }

    public decimal FeeEstimate { get; private set; }

    public decimal SlippageBuffer { get; private set; }

    public decimal NetEdge { get; private set; }

    public decimal ExecutableCapacity { get; private set; }

    public bool CanPromote { get; private set; }

    public string CalibrationBucket { get; private set; }

    public string ComponentsJson { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    private static decimal RequireProbability(decimal value, string paramName)
    {
        if (value < 0m || value > 1m)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Probability must be in 0..1.");
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
