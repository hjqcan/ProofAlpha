using NetDevPack.Domain;

namespace Autotrade.Strategy.Domain.Entities;

public sealed class StrategyObservationLog : Entity, IAggregateRoot
{
    private StrategyObservationLog()
    {
        StrategyId = string.Empty;
        Phase = string.Empty;
        Outcome = string.Empty;
        ReasonCode = string.Empty;
        ConfigVersion = string.Empty;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public StrategyObservationLog(
        string strategyId,
        string? marketId,
        string phase,
        string outcome,
        string reasonCode,
        string? featuresJson,
        string? stateJson,
        string? correlationId,
        string configVersion,
        string? executionMode,
        DateTimeOffset createdAtUtc)
    {
        StrategyId = string.IsNullOrWhiteSpace(strategyId)
            ? throw new ArgumentException("StrategyId cannot be empty.", nameof(strategyId))
            : strategyId.Trim();

        MarketId = string.IsNullOrWhiteSpace(marketId) ? null : marketId.Trim();

        Phase = string.IsNullOrWhiteSpace(phase)
            ? throw new ArgumentException("Phase cannot be empty.", nameof(phase))
            : phase.Trim();

        Outcome = string.IsNullOrWhiteSpace(outcome)
            ? throw new ArgumentException("Outcome cannot be empty.", nameof(outcome))
            : outcome.Trim();

        ReasonCode = string.IsNullOrWhiteSpace(reasonCode)
            ? throw new ArgumentException("ReasonCode cannot be empty.", nameof(reasonCode))
            : reasonCode.Trim();

        FeaturesJson = string.IsNullOrWhiteSpace(featuresJson) ? null : featuresJson.Trim();
        StateJson = string.IsNullOrWhiteSpace(stateJson) ? null : stateJson.Trim();
        CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim();

        ConfigVersion = string.IsNullOrWhiteSpace(configVersion)
            ? throw new ArgumentException("ConfigVersion cannot be empty.", nameof(configVersion))
            : configVersion.Trim();

        ExecutionMode = string.IsNullOrWhiteSpace(executionMode) ? null : executionMode.Trim();
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc;
    }

    public string StrategyId { get; private set; }

    public string? MarketId { get; private set; }

    public string Phase { get; private set; }

    public string Outcome { get; private set; }

    public string ReasonCode { get; private set; }

    public string? FeaturesJson { get; private set; }

    public string? StateJson { get; private set; }

    public string? CorrelationId { get; private set; }

    public string ConfigVersion { get; private set; }

    public string? ExecutionMode { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
