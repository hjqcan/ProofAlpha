using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Trading.Application.Contract.Execution;

namespace Autotrade.Api.ControlRoom;

public sealed record ControlRoomSnapshotResponse(
    DateTimeOffset TimestampUtc,
    string DataMode,
    string CommandMode,
    ControlRoomProcessDto Process,
    ControlRoomRiskDto Risk,
    IReadOnlyList<ControlRoomMetricDto> Metrics,
    IReadOnlyList<ControlRoomStrategyDto> Strategies,
    IReadOnlyList<ControlRoomMarketDto> Markets,
    IReadOnlyList<ControlRoomOrderDto> Orders,
    IReadOnlyList<ControlRoomPositionDto> Positions,
    IReadOnlyList<ControlRoomDecisionDto> Decisions,
    IReadOnlyList<ControlRoomTimelineItemDto> Timeline,
    IReadOnlyList<ControlRoomSeriesPointDto> CapitalCurve,
    IReadOnlyList<ControlRoomSeriesPointDto> LatencyCurve);

public sealed record ControlRoomProcessDto(
    string ApiStatus,
    string Environment,
    string ExecutionMode,
    bool ModulesEnabled,
    int ReadyChecks,
    int DegradedChecks,
    int UnhealthyChecks);

public sealed record ControlRoomRiskDto(
    bool KillSwitchActive,
    string KillSwitchLevel,
    string? KillSwitchReason,
    DateTimeOffset? KillSwitchActivatedAtUtc,
    decimal TotalCapital,
    decimal AvailableCapital,
    decimal CapitalUtilizationPct,
    decimal OpenNotional,
    int OpenOrders,
    int UnhedgedExposures,
    IReadOnlyList<ControlRoomRiskLimitDto> Limits);

public sealed record ControlRoomRiskLimitDto(
    string Name,
    decimal Current,
    decimal Limit,
    string Unit,
    string State);

public sealed record ControlRoomMetricDto(
    string Label,
    string Value,
    string Delta,
    string Tone);

public sealed record ControlRoomStrategyDto(
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
    ControlRoomStrategyBlockedReasonDto? BlockedReason,
    IReadOnlyList<ControlRoomParameterDto> Parameters)
{
    public string ModelVersion { get; init; } = "api-control-room-legacy.v1";

    public string SourceVersion { get; init; } = "api-control-room-legacy.v1";
}

public sealed record ControlRoomStrategyBlockedReasonDto(
    string Kind,
    string Code,
    string Message);

public sealed record ControlRoomParameterDto(
    string Name,
    string Value);

public sealed record ControlRoomMarketDto(
    string MarketId,
    string ConditionId,
    string Name,
    string Category,
    string Status,
    decimal? YesPrice,
    decimal? NoPrice,
    decimal Liquidity,
    decimal Volume24h,
    DateTimeOffset? ExpiresAtUtc,
    decimal SignalScore,
    string? Slug,
    string? Description,
    bool AcceptingOrders,
    IReadOnlyList<ControlRoomMarketTokenDto> Tokens,
    IReadOnlyList<string> Tags,
    decimal? Spread,
    string Source,
    decimal RankScore,
    string RankReason,
    IReadOnlyList<string> UnsuitableReasons);

public sealed record ControlRoomMarketTokenDto(
    string TokenId,
    string Outcome,
    decimal? Price,
    bool? Winner);

public sealed record ControlRoomMarketsResponse(
    DateTimeOffset TimestampUtc,
    string Source,
    int TotalCount,
    bool IsComplete,
    IReadOnlyList<string> Categories,
    IReadOnlyList<ControlRoomMarketDto> Markets);

public sealed record ControlRoomMarketDiscoveryQuery(
    string? Search = null,
    string? Category = null,
    string? Status = null,
    string? Sort = null,
    decimal? MinLiquidity = null,
    decimal? MinVolume24h = null,
    int? MaxDaysToExpiry = null,
    bool? AcceptingOrders = null,
    decimal? MinSignalScore = null,
    int? Limit = null,
    int? Offset = null);

public sealed record ControlRoomMarketDetailResponse(
    DateTimeOffset TimestampUtc,
    string Source,
    ControlRoomMarketDto Market,
    ControlRoomOrderBookDto? OrderBook,
    IReadOnlyList<ControlRoomOrderDto> Orders,
    IReadOnlyList<ControlRoomPositionDto> Positions,
    IReadOnlyList<ControlRoomDecisionDto> Decisions,
    IReadOnlyList<ControlRoomMetricDto> Microstructure);

public sealed record ControlRoomOrderBookDto(
    string MarketId,
    string TokenId,
    string Outcome,
    DateTimeOffset LastUpdatedUtc,
    decimal? BestBidPrice,
    decimal? BestBidSize,
    decimal? BestAskPrice,
    decimal? BestAskSize,
    decimal? Spread,
    decimal? Midpoint,
    decimal TotalBidSize,
    decimal TotalAskSize,
    decimal ImbalancePct,
    decimal MaxLevelNotional,
    string Source,
    ControlRoomOrderBookFreshnessDto Freshness,
    IReadOnlyList<ControlRoomOrderBookLevelDto> Bids,
    IReadOnlyList<ControlRoomOrderBookLevelDto> Asks);

public sealed record ControlRoomOrderBookFreshnessDto(
    string Status,
    int AgeSeconds,
    int FreshSeconds,
    int StaleSeconds,
    string Message);

public sealed record ControlRoomOrderBookLevelDto(
    int Level,
    decimal Price,
    decimal Size,
    decimal Notional,
    decimal DepthPct);

public sealed record ControlRoomOrderDto(
    string ClientOrderId,
    string StrategyId,
    string MarketId,
    string Side,
    string Outcome,
    decimal Price,
    decimal Quantity,
    decimal FilledQuantity,
    string Status,
    DateTimeOffset UpdatedAtUtc);

public sealed record ControlRoomPositionDto(
    string MarketId,
    string Outcome,
    decimal Quantity,
    decimal AverageCost,
    decimal Notional,
    decimal RealizedPnl,
    decimal? MarkPrice,
    decimal? UnrealizedPnl,
    decimal? TotalPnl,
    decimal? ReturnPct,
    string MarkSource,
    DateTimeOffset UpdatedAtUtc);

public sealed record ControlRoomDecisionDto(
    string StrategyId,
    string Action,
    string MarketId,
    string Reason,
    DateTimeOffset CreatedAtUtc);

public sealed record ControlRoomTimelineItemDto(
    DateTimeOffset TimestampUtc,
    string Label,
    string Detail,
    string Tone);

public sealed record ControlRoomSeriesPointDto(
    DateTimeOffset TimestampUtc,
    decimal Value);

public sealed record SetStrategyStateRequest(
    string TargetState,
    string? Actor = null,
    string? ReasonCode = null,
    string? Reason = null,
    string? ConfirmationText = null);

public sealed record SetKillSwitchRequest(
    bool Active,
    string? Level,
    string? ReasonCode,
    string? Reason,
    string? Actor = null,
    string? ConfirmationText = null);

public sealed record ArmLiveRequest(
    string? Actor = null,
    string? Reason = null,
    string? ConfirmationText = null);

public sealed record DisarmLiveRequest(
    string? Actor = null,
    string? Reason = null,
    string? ConfirmationText = null);

public sealed record CancelOpenOrdersRequest(
    string? Actor = null,
    string? ReasonCode = null,
    string? Reason = null,
    string? StrategyId = null,
    string? MarketId = null,
    string? ConfirmationText = null);

public sealed record IncidentPackageQuery(
    string? RiskEventId = null,
    string? StrategyId = null,
    string? MarketId = null,
    string? OrderId = null,
    string? CorrelationId = null);

public sealed record IncidentActionCatalog(
    DateTimeOffset GeneratedAtUtc,
    string CommandMode,
    string RunbookPath,
    IReadOnlyList<IncidentActionDescriptor> Actions);

public sealed record IncidentActionDescriptor(
    string Id,
    string Label,
    string Category,
    string Scope,
    string Method,
    string Path,
    bool Enabled,
    string? DisabledReason,
    string? ConfirmationText,
    string Result);

public sealed record IncidentPackage(
    DateTimeOffset GeneratedAtUtc,
    string ContractVersion,
    IncidentPackageQuery Query,
    ControlRoomSnapshotResponse Snapshot,
    IncidentActionCatalog Actions,
    IReadOnlyList<string> RunbookReferences,
    IReadOnlyList<string> ExportReferences);

public sealed record LiveArmingStatusResponse(
    LiveArmingStatus Status);

public sealed record ControlRoomCommandResponse(
    string Status,
    string CommandMode,
    string Message,
    ControlRoomSnapshotResponse Snapshot);
