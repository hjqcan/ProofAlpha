using Autotrade.Application.Readiness;
using Autotrade.MarketData.Application.Contract.Tape;
using Autotrade.Strategy.Application.Decisions;
using Autotrade.Strategy.Application.RunSessions;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Strategy.Application.Audit;

public interface IReplayExportService
{
    Task<ReplayExportPackage> ExportAsync(
        ReplayExportQuery query,
        CancellationToken cancellationToken = default);
}

public sealed record ReplayExportQuery(
    string? StrategyId = null,
    string? MarketId = null,
    Guid? OrderId = null,
    string? ClientOrderId = null,
    Guid? RunSessionId = null,
    Guid? RiskEventId = null,
    string? CorrelationId = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    int Limit = 1000);

public sealed record ReplayExportPackage(
    DateTimeOffset GeneratedAtUtc,
    string ContractVersion,
    ReplayExportQuery Query,
    ReplayRedactionSummary Redaction,
    IReadOnlyList<string> CompletenessNotes,
    ReplayRunSessionRecord? RunSession,
    AuditTimeline Timeline,
    ReplayEvidenceBundle Evidence,
    ReplayMarketTapeSlice? MarketTape,
    IReadOnlyList<ReplayStrategyConfigVersion> StrategyConfigVersions,
    ReplayReadinessReport? Readiness,
    ReplayExportReferences ExportReferences);

public sealed record ReplayMarketTapeSlice(
    MarketTapeQuery Query,
    IReadOnlyList<MarketPriceTickDto> PriceTicks,
    IReadOnlyList<OrderBookTopTickDto> TopTicks,
    IReadOnlyList<OrderBookDepthSnapshotDto> DepthSnapshots,
    IReadOnlyList<ClobTradeTickDto> TradeTicks,
    IReadOnlyList<MarketResolutionEventDto> ResolutionEvents,
    IReadOnlyList<string> CompletenessNotes);

public sealed record ReplayRedactionSummary(
    IReadOnlyList<string> AppliedRules,
    IReadOnlyList<string> ExcludedFields);

public sealed record ReplayRunSessionRecord(
    Guid SessionId,
    string ExecutionMode,
    string ConfigVersion,
    IReadOnlyList<string> Strategies,
    string RiskProfileJson,
    string OperatorSource,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? StoppedAtUtc,
    string? StopReason,
    bool IsActive,
    bool Recovered);

public sealed record ReplayEvidenceBundle(
    IReadOnlyList<ReplayDecisionRecord> Decisions,
    IReadOnlyList<ReplayOrderEventRecord> OrderEvents,
    IReadOnlyList<ReplayOrderRecord> Orders,
    IReadOnlyList<ReplayTradeRecord> Trades,
    IReadOnlyList<ReplayPositionRecord> Positions,
    IReadOnlyList<ReplayRiskEventRecord> RiskEvents);

public sealed record ReplayDecisionRecord(
    Guid DecisionId,
    string StrategyId,
    string Action,
    string Reason,
    string? MarketId,
    string? ContextJson,
    DateTimeOffset TimestampUtc,
    string ConfigVersion,
    string? CorrelationId,
    string? ExecutionMode,
    Guid? RunSessionId);

public sealed record ReplayOrderEventRecord(
    Guid Id,
    Guid OrderId,
    string ClientOrderId,
    string StrategyId,
    string MarketId,
    OrderEventType EventType,
    OrderStatus Status,
    string Message,
    string? ContextJson,
    string? CorrelationId,
    DateTimeOffset CreatedAtUtc,
    Guid? RunSessionId);

public sealed record ReplayOrderRecord(
    Guid Id,
    string MarketId,
    string? TokenId,
    string? StrategyId,
    string? ClientOrderId,
    string? ExchangeOrderId,
    string? CorrelationId,
    OutcomeSide Outcome,
    OrderSide Side,
    OrderType OrderType,
    TimeInForce TimeInForce,
    DateTimeOffset? GoodTilDateUtc,
    bool NegRisk,
    decimal Price,
    decimal Quantity,
    decimal FilledQuantity,
    OrderStatus Status,
    string? RejectionReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ReplayTradeRecord(
    Guid Id,
    Guid OrderId,
    string ClientOrderId,
    string StrategyId,
    string MarketId,
    string TokenId,
    OutcomeSide Outcome,
    OrderSide Side,
    decimal Price,
    decimal Quantity,
    string ExchangeTradeId,
    decimal Fee,
    decimal Notional,
    string? CorrelationId,
    DateTimeOffset CreatedAtUtc);

public sealed record ReplayPositionRecord(
    Guid Id,
    string MarketId,
    OutcomeSide Outcome,
    decimal Quantity,
    decimal AverageCost,
    decimal RealizedPnl,
    decimal Notional,
    DateTimeOffset UpdatedAtUtc);

public sealed record ReplayRiskEventRecord(
    Guid Id,
    string Code,
    RiskSeverity Severity,
    string Message,
    string? StrategyId,
    string? ContextJson,
    DateTimeOffset CreatedAtUtc,
    string? MarketId);

public sealed record ReplayStrategyConfigVersion(
    string StrategyId,
    string ConfigVersion,
    string Source,
    DateTimeOffset? ObservedAtUtc);

public sealed record ReplayReadinessReport(
    string ContractVersion,
    DateTimeOffset CheckedAtUtc,
    ReadinessOverallStatus Status,
    IReadOnlyList<ReplayReadinessCheck> Checks,
    IReadOnlyList<ReplayReadinessCapability> Capabilities);

public sealed record ReplayReadinessCheck(
    string Id,
    ReadinessCheckCategory Category,
    ReadinessCheckRequirement Requirement,
    ReadinessCheckStatus Status,
    string Source,
    DateTimeOffset LastCheckedAtUtc,
    string Summary,
    string RemediationHint,
    IReadOnlyDictionary<string, string> Evidence);

public sealed record ReplayReadinessCapability(
    ReadinessCapability Capability,
    ReadinessOverallStatus Status,
    IReadOnlyList<string> BlockingCheckIds,
    string Summary);

public sealed record ReplayExportReferences(
    string JsonApi,
    string JsonCli,
    string Schema);
