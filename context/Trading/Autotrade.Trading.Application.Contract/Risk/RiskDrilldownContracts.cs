namespace Autotrade.Trading.Application.Contract.Risk;

public sealed record RiskDrilldownQuery(
    string? StrategyId = null,
    string? MarketId = null,
    Guid? RiskEventId = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    int Limit = 100);

public interface IRiskDrilldownService
{
    Task<RiskEventDrilldown?> GetRiskEventAsync(
        Guid riskEventId,
        CancellationToken cancellationToken = default);

    Task<UnhedgedExposureDrilldownResponse> QueryUnhedgedExposuresAsync(
        RiskDrilldownQuery query,
        CancellationToken cancellationToken = default);
}

public sealed record RiskEventDrilldown(
    DateTimeOffset GeneratedAtUtc,
    RiskEventRecord Event,
    RiskTriggerDrilldown Trigger,
    RiskActionDrilldown Action,
    IReadOnlyList<RiskAffectedOrder> AffectedOrders,
    UnhedgedExposureDrilldown? Exposure,
    RiskKillSwitchLink? KillSwitch,
    RiskDrilldownSourceReferences SourceReferences);

public sealed record RiskTriggerDrilldown(
    string TriggerReason,
    string? LimitName,
    decimal? CurrentValue,
    decimal? Threshold,
    string? Unit,
    string State);

public sealed record RiskActionDrilldown(
    string SelectedAction,
    string? MitigationResult,
    string? ReasonCode);

public sealed record RiskAffectedOrder(
    Guid? OrderId,
    string? ClientOrderId,
    string? StrategyId,
    string? MarketId,
    string? Status,
    string Source,
    string DetailReference);

public sealed record UnhedgedExposureDrilldown(
    Guid? EvidenceId,
    string StrategyId,
    string MarketId,
    string TokenId,
    string HedgeTokenId,
    string Outcome,
    string Side,
    decimal Quantity,
    decimal Price,
    decimal Notional,
    double DurationSeconds,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    double? TimeoutSeconds,
    string HedgeState,
    string MitigationResult,
    string Source);

public sealed record RiskKillSwitchLink(
    string Scope,
    string Level,
    string ReasonCode,
    string Reason,
    DateTimeOffset? ActivatedAtUtc,
    Guid? TriggeringRiskEventId);

public sealed record RiskDrilldownSourceReferences(
    string JsonApi,
    string CsvApi,
    IReadOnlyList<Guid> RiskEventIds,
    IReadOnlyList<Guid> OrderEventIds);

public sealed record UnhedgedExposureDrilldownResponse(
    DateTimeOffset GeneratedAtUtc,
    int Count,
    int Limit,
    RiskDrilldownQuery Query,
    IReadOnlyList<UnhedgedExposureDrilldown> Exposures);
