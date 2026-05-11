using Autotrade.Strategy.Application.Decisions;
using Autotrade.Strategy.Application.RunSessions;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.Contract.Risk;

namespace Autotrade.Strategy.Application.RunReports;

public interface IPaperRunReportService
{
    Task<PaperRunReport?> GetAsync(
        Guid sessionId,
        int limit = 1000,
        CancellationToken cancellationToken = default);
}

public sealed record PaperRunReport(
    DateTimeOffset GeneratedAtUtc,
    string ReportStatus,
    IReadOnlyList<string> CompletenessNotes,
    PaperRunSessionRecord Session,
    PaperRunReportSummary Summary,
    IReadOnlyList<PaperRunStrategyBreakdown> StrategyBreakdown,
    IReadOnlyList<PaperRunMarketBreakdown> MarketBreakdown,
    IReadOnlyList<RiskEventRecord> RiskEvents,
    IReadOnlyList<PaperRunIncident> NotableIncidents,
    PaperRunEvidenceLinks EvidenceLinks,
    PaperRunExportReferences ExportReferences)
{
    public PaperRunAttribution Attribution { get; init; } = PaperRunAttribution.Empty;
}

public sealed record PaperRunReportSummary(
    int DecisionCount,
    int OrderEventCount,
    int OrderCount,
    int TradeCount,
    int PositionCount,
    int RiskEventCount,
    int FilledOrderEventCount,
    int RejectedOrderEventCount,
    decimal TotalBuyNotional,
    decimal TotalSellNotional,
    decimal TotalFees,
    decimal GrossPnl,
    decimal NetPnl);

public sealed record PaperRunStrategyBreakdown(
    string StrategyId,
    int DecisionCount,
    int OrderEventCount,
    int OrderCount,
    int TradeCount,
    int RiskEventCount,
    decimal TotalBuyNotional,
    decimal TotalSellNotional,
    decimal TotalFees,
    decimal NetPnl)
{
    public decimal RealizedPnl { get; init; } = NetPnl;

    public decimal? UnrealizedPnl { get; init; }

    public decimal EstimatedSlippage { get; init; }

    public double? AverageDecisionToFillLatencyMs { get; init; }

    public int StaleDataEventCount { get; init; }

    public decimal UnhedgedExposureNotional { get; init; }

    public double UnhedgedExposureSeconds { get; init; }
}

public sealed record PaperRunMarketBreakdown(
    string MarketId,
    int DecisionCount,
    int OrderEventCount,
    int OrderCount,
    int TradeCount,
    int PositionCount,
    decimal TotalBuyNotional,
    decimal TotalSellNotional,
    decimal NetPnl)
{
    public decimal RealizedPnl { get; init; } = NetPnl;

    public decimal? UnrealizedPnl { get; init; }

    public decimal EstimatedSlippage { get; init; }

    public double? AverageDecisionToFillLatencyMs { get; init; }

    public int StaleDataEventCount { get; init; }

    public decimal UnhedgedExposureNotional { get; init; }

    public double UnhedgedExposureSeconds { get; init; }
}

public sealed record PaperRunAttribution(
    PaperRunPnlAttribution Pnl,
    PaperRunSlippageAttribution Slippage,
    PaperRunLatencyAttribution Latency,
    PaperRunStaleDataAttribution StaleData,
    PaperRunUnhedgedExposureAttribution UnhedgedExposure,
    bool StrategyTotalsReconcile,
    bool MarketTotalsReconcile,
    IReadOnlyList<string> ReconciliationNotes)
{
    public static PaperRunAttribution Empty { get; } = new(
        PaperRunPnlAttribution.Empty,
        PaperRunSlippageAttribution.Empty,
        PaperRunLatencyAttribution.Empty,
        PaperRunStaleDataAttribution.Empty,
        PaperRunUnhedgedExposureAttribution.Empty,
        StrategyTotalsReconcile: true,
        MarketTotalsReconcile: true,
        ReconciliationNotes: []);
}

public sealed record PaperRunPnlAttribution(
    decimal RealizedPnl,
    decimal? UnrealizedPnl,
    decimal Fees,
    decimal GrossPnl,
    decimal NetPnl,
    string RealizedPnlSource,
    string UnrealizedPnlSource,
    string FeeSource,
    string MarkSource,
    IReadOnlyList<string> Notes)
{
    public static PaperRunPnlAttribution Empty { get; } = new(
        0m,
        null,
        0m,
        0m,
        0m,
        "none",
        "unavailable",
        "none",
        "unavailable",
        []);
}

public sealed record PaperRunSlippageAttribution(
    decimal EstimatedSlippage,
    decimal AdverseSlippage,
    decimal FavorablePriceImprovement,
    string Source,
    int TradeCountWithEstimate,
    int TradeCountWithoutEstimate,
    IReadOnlyList<Guid> EvidenceIds,
    IReadOnlyList<string> Notes)
{
    public static PaperRunSlippageAttribution Empty { get; } = new(
        0m,
        0m,
        0m,
        "unavailable",
        0,
        0,
        [],
        []);
}

public sealed record PaperRunLatencyAttribution(
    double? AverageDecisionToFillLatencyMs,
    double? P95DecisionToFillLatencyMs,
    double? AverageAcceptedToFillLatencyMs,
    int FillEventCountWithDecisionLatency,
    int FillEventCountWithAcceptedLatency,
    IReadOnlyList<Guid> EvidenceIds,
    IReadOnlyList<string> Notes)
{
    public static PaperRunLatencyAttribution Empty { get; } = new(
        null,
        null,
        null,
        0,
        0,
        [],
        []);
}

public sealed record PaperRunStaleDataAttribution(
    int EventCount,
    decimal? EstimatedPnlContribution,
    string Source,
    IReadOnlyList<Guid> EvidenceIds,
    IReadOnlyList<string> Notes)
{
    public static PaperRunStaleDataAttribution Empty { get; } = new(
        0,
        null,
        "risk_events",
        [],
        []);
}

public sealed record PaperRunUnhedgedExposureAttribution(
    int EventCount,
    decimal TotalNotional,
    double TotalDurationSeconds,
    double? AverageDurationSeconds,
    IReadOnlyList<PaperRunUnhedgedExposureRecord> Exposures,
    IReadOnlyList<string> Notes)
{
    public static PaperRunUnhedgedExposureAttribution Empty { get; } = new(
        0,
        0m,
        0d,
        null,
        [],
        []);
}

public sealed record PaperRunUnhedgedExposureRecord(
    Guid EvidenceId,
    string? StrategyId,
    string? MarketId,
    decimal Notional,
    double DurationSeconds,
    string MitigationOutcome,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? EndedAtUtc);

public sealed record PaperRunIncident(
    DateTimeOffset TimestampUtc,
    string Source,
    string Severity,
    string Code,
    string Message,
    string? StrategyId,
    string? MarketId,
    Guid EvidenceId);

public sealed record PaperRunEvidenceLinks(
    IReadOnlyList<Guid> DecisionIds,
    IReadOnlyList<Guid> OrderEventIds,
    IReadOnlyList<Guid> OrderIds,
    IReadOnlyList<Guid> TradeIds,
    IReadOnlyList<Guid> PositionIds,
    IReadOnlyList<Guid> RiskEventIds);

public sealed record PaperRunExportReferences(
    string JsonApi,
    string JsonCli,
    string CsvCli,
    IReadOnlyList<string> CsvTables);

internal sealed record PaperRunReportEvidence(
    IReadOnlyList<StrategyDecisionRecord> Decisions,
    IReadOnlyList<OrderEventDto> OrderEvents,
    IReadOnlyList<OrderDto> Orders,
    IReadOnlyList<TradeDto> Trades,
    IReadOnlyList<PositionDto> Positions,
    IReadOnlyList<RiskEventRecord> RiskEvents);
