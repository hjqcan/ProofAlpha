using NetDevPack.Domain;

namespace Autotrade.Strategy.Domain.Entities;

public sealed class StrategyDecisionLog : Entity, IAggregateRoot
{
    // EF Core
    private StrategyDecisionLog()
    {
        StrategyId = string.Empty;
        Action = string.Empty;
        Reason = string.Empty;
        ConfigVersion = string.Empty;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public StrategyDecisionLog(
        string strategyId,
        string action,
        string reason,
        string? marketId,
        string? contextJson,
        DateTimeOffset createdAtUtc,
        string configVersion,
        string? correlationId = null,
        string? executionMode = null,
        Guid? runSessionId = null)
    {
        StrategyId = string.IsNullOrWhiteSpace(strategyId)
            ? throw new ArgumentException("StrategyId cannot be empty.", nameof(strategyId))
            : strategyId.Trim();

        Action = string.IsNullOrWhiteSpace(action)
            ? throw new ArgumentException("Action cannot be empty.", nameof(action))
            : action.Trim();

        Reason = string.IsNullOrWhiteSpace(reason)
            ? throw new ArgumentException("Reason cannot be empty.", nameof(reason))
            : reason.Trim();

        MarketId = string.IsNullOrWhiteSpace(marketId) ? null : marketId.Trim();
        ContextJson = string.IsNullOrWhiteSpace(contextJson) ? null : contextJson.Trim();
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc;

        ConfigVersion = string.IsNullOrWhiteSpace(configVersion)
            ? throw new ArgumentException("ConfigVersion cannot be empty.", nameof(configVersion))
            : configVersion.Trim();

        CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim();
        ExecutionMode = string.IsNullOrWhiteSpace(executionMode) ? null : executionMode.Trim();
        RunSessionId = runSessionId;
    }

    public string StrategyId { get; private set; }

    public string Action { get; private set; }

    public string Reason { get; private set; }

    public string? MarketId { get; private set; }

    public string? ContextJson { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public string ConfigVersion { get; private set; }

    /// <summary>
    /// 关联 ID（用于跟踪）。
    /// </summary>
    public string? CorrelationId { get; private set; }

    /// <summary>
    /// 执行模式（Live/Paper）。
    /// </summary>
    public string? ExecutionMode { get; private set; }

    public Guid? RunSessionId { get; private set; }
}
