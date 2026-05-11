using Autotrade.Strategy.Application.Contract.Strategies;

namespace Autotrade.Strategy.Application.Contract.ControlRoom;

public interface IStrategyControlRoomReadModelProvider
{
    Task<StrategyControlRoomReadModel> GetReadModelAsync(CancellationToken cancellationToken = default);
}

public sealed record StrategyControlRoomReadModel(
    string ModelVersion,
    string SourceVersion,
    IReadOnlyList<StrategyControlRoomCard> Strategies);

public sealed record StrategyControlRoomCard(
    string StrategyId,
    string Name,
    StrategyState State,
    bool Enabled,
    string ConfigVersion,
    string DesiredState,
    int ActiveMarkets,
    long CycleCount,
    long SnapshotsProcessed,
    int ChannelBacklog,
    bool IsKillSwitchBlocked,
    DateTimeOffset? LastHeartbeatUtc,
    DateTimeOffset? LastDecisionAtUtc,
    string? LastError,
    StrategyBlockedReason? BlockedReason,
    IReadOnlyList<StrategyControlRoomParameter> Parameters,
    string ModelVersion,
    string SourceVersion);

public sealed record StrategyControlRoomParameter(
    string Name,
    string Value);
