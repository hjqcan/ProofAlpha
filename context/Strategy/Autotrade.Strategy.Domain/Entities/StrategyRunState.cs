using NetDevPack.Domain;

namespace Autotrade.Strategy.Domain.Entities;

public sealed class StrategyRunState : Entity, IAggregateRoot
{
    // EF Core
    private StrategyRunState()
    {
        StrategyId = string.Empty;
        Name = string.Empty;
        State = string.Empty;
        DesiredState = string.Empty;
        ConfigVersion = string.Empty;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public StrategyRunState(
        string strategyId,
        string name,
        string state,
        bool enabled,
        string configVersion,
        int restartCount,
        DateTimeOffset? startedAtUtc,
        DateTimeOffset? lastDecisionAtUtc,
        DateTimeOffset? lastHeartbeatUtc,
        string? lastError,
        string? activeMarketsJson = null,
        long cycleCount = 0,
        long snapshotsProcessed = 0,
        int channelBacklog = 0,
        string? desiredState = null,
        string? blockedReasonKind = null,
        string? blockedReasonCode = null,
        string? blockedReasonMessage = null)
    {
        StrategyId = string.IsNullOrWhiteSpace(strategyId)
            ? throw new ArgumentException("StrategyId cannot be empty.", nameof(strategyId))
            : strategyId.Trim();

        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Name cannot be empty.", nameof(name))
            : name.Trim();

        State = string.IsNullOrWhiteSpace(state)
            ? throw new ArgumentException("State cannot be empty.", nameof(state))
            : state.Trim();
        DesiredState = string.IsNullOrWhiteSpace(desiredState) ? State : desiredState.Trim();

        Enabled = enabled;
        ConfigVersion = string.IsNullOrWhiteSpace(configVersion)
            ? throw new ArgumentException("ConfigVersion cannot be empty.", nameof(configVersion))
            : configVersion.Trim();

        RestartCount = restartCount < 0
            ? throw new ArgumentOutOfRangeException(nameof(restartCount), restartCount, "RestartCount cannot be negative.")
            : restartCount;

        StartedAtUtc = startedAtUtc;
        LastDecisionAtUtc = lastDecisionAtUtc;
        LastHeartbeatAtUtc = lastHeartbeatUtc;
        LastError = string.IsNullOrWhiteSpace(lastError) ? null : lastError.Trim();
        ActiveMarketsJson = activeMarketsJson;
        CycleCount = cycleCount;
        SnapshotsProcessed = snapshotsProcessed;
        ChannelBacklog = channelBacklog;
        BlockedReasonKind = string.IsNullOrWhiteSpace(blockedReasonKind) ? null : blockedReasonKind.Trim();
        BlockedReasonCode = string.IsNullOrWhiteSpace(blockedReasonCode) ? null : blockedReasonCode.Trim();
        BlockedReasonMessage = string.IsNullOrWhiteSpace(blockedReasonMessage) ? null : blockedReasonMessage.Trim();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public string StrategyId { get; private set; }

    public string Name { get; private set; }

    public string State { get; private set; }

    public string DesiredState { get; private set; }

    public bool Enabled { get; private set; }

    public string ConfigVersion { get; private set; }

    public int RestartCount { get; private set; }

    public DateTimeOffset? StartedAtUtc { get; private set; }

    public DateTimeOffset? LastDecisionAtUtc { get; private set; }

    public DateTimeOffset? LastHeartbeatAtUtc { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>
    /// JSON array of active market IDs (e.g., ["mkt-1","mkt-2"]).
    /// </summary>
    public string? ActiveMarketsJson { get; private set; }

    public long CycleCount { get; private set; }

    public long SnapshotsProcessed { get; private set; }

    public int ChannelBacklog { get; private set; }

    public string? BlockedReasonKind { get; private set; }

    public string? BlockedReasonCode { get; private set; }

    public string? BlockedReasonMessage { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Update(
        string state,
        bool enabled,
        string configVersion,
        int restartCount,
        DateTimeOffset? startedAtUtc,
        DateTimeOffset? lastDecisionAtUtc,
        DateTimeOffset? lastHeartbeatUtc,
        string? lastError,
        string? activeMarketsJson = null,
        long cycleCount = 0,
        long snapshotsProcessed = 0,
        int channelBacklog = 0,
        string? desiredState = null,
        string? blockedReasonKind = null,
        string? blockedReasonCode = null,
        string? blockedReasonMessage = null)
    {
        State = string.IsNullOrWhiteSpace(state) ? State : state.Trim();
        DesiredState = string.IsNullOrWhiteSpace(desiredState) ? DesiredState : desiredState.Trim();
        Enabled = enabled;
        ConfigVersion = string.IsNullOrWhiteSpace(configVersion) ? ConfigVersion : configVersion.Trim();
        RestartCount = restartCount < 0 ? RestartCount : restartCount;
        StartedAtUtc = startedAtUtc;
        LastDecisionAtUtc = lastDecisionAtUtc;
        LastHeartbeatAtUtc = lastHeartbeatUtc;
        LastError = string.IsNullOrWhiteSpace(lastError) ? null : lastError.Trim();
        ActiveMarketsJson = activeMarketsJson;
        CycleCount = cycleCount;
        SnapshotsProcessed = snapshotsProcessed;
        ChannelBacklog = channelBacklog;
        BlockedReasonKind = string.IsNullOrWhiteSpace(blockedReasonKind) ? null : blockedReasonKind.Trim();
        BlockedReasonCode = string.IsNullOrWhiteSpace(blockedReasonCode) ? null : blockedReasonCode.Trim();
        BlockedReasonMessage = string.IsNullOrWhiteSpace(blockedReasonMessage) ? null : blockedReasonMessage.Trim();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
