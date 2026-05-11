using System.Globalization;
using System.Text;

namespace Autotrade.Strategy.Application.RunReports;

public static class PaperRunReportCsvFormatter
{
    public static string Format(PaperRunReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var sb = new StringBuilder();
        AppendSummary(sb, report);
        AppendStrategyBreakdown(sb, report);
        AppendMarketBreakdown(sb, report);
        AppendAttribution(sb, report);
        AppendUnhedgedExposures(sb, report);
        AppendRiskEvents(sb, report);
        AppendIncidents(sb, report);
        AppendEvidence(sb, report);
        return sb.ToString();
    }

    private static void AppendSummary(StringBuilder sb, PaperRunReport report)
    {
        sb.AppendLine("table,summary");
        sb.AppendLine("generated_at_utc,session_id,status,execution_mode,config_version,started_at_utc,stopped_at_utc,decision_count,order_event_count,order_count,trade_count,position_count,risk_event_count,total_buy_notional,total_sell_notional,total_fees,gross_pnl,net_pnl,notes");
        sb.AppendLine(string.Join(",",
            Timestamp(report.GeneratedAtUtc),
            report.Session.SessionId,
            Csv(report.ReportStatus),
            Csv(report.Session.ExecutionMode),
            Csv(report.Session.ConfigVersion),
            Timestamp(report.Session.StartedAtUtc),
            Timestamp(report.Session.StoppedAtUtc),
            report.Summary.DecisionCount,
            report.Summary.OrderEventCount,
            report.Summary.OrderCount,
            report.Summary.TradeCount,
            report.Summary.PositionCount,
            report.Summary.RiskEventCount,
            Number(report.Summary.TotalBuyNotional),
            Number(report.Summary.TotalSellNotional),
            Number(report.Summary.TotalFees),
            Number(report.Summary.GrossPnl),
            Number(report.Summary.NetPnl),
            Csv(string.Join(" | ", report.CompletenessNotes))));
        sb.AppendLine();
    }

    private static void AppendStrategyBreakdown(StringBuilder sb, PaperRunReport report)
    {
        sb.AppendLine("table,strategies");
        sb.AppendLine("strategy_id,decision_count,order_event_count,order_count,trade_count,risk_event_count,total_buy_notional,total_sell_notional,total_fees,net_pnl,realized_pnl,unrealized_pnl,estimated_slippage,avg_decision_to_fill_latency_ms,stale_data_event_count,unhedged_exposure_notional,unhedged_exposure_seconds");
        foreach (var item in report.StrategyBreakdown)
        {
            sb.AppendLine(string.Join(",",
                Csv(item.StrategyId),
                item.DecisionCount,
                item.OrderEventCount,
                item.OrderCount,
                item.TradeCount,
                item.RiskEventCount,
                Number(item.TotalBuyNotional),
                Number(item.TotalSellNotional),
                Number(item.TotalFees),
                Number(item.NetPnl),
                Number(item.RealizedPnl),
                Number(item.UnrealizedPnl),
                Number(item.EstimatedSlippage),
                Number(item.AverageDecisionToFillLatencyMs),
                item.StaleDataEventCount,
                Number(item.UnhedgedExposureNotional),
                Number(item.UnhedgedExposureSeconds)));
        }

        sb.AppendLine();
    }

    private static void AppendMarketBreakdown(StringBuilder sb, PaperRunReport report)
    {
        sb.AppendLine("table,markets");
        sb.AppendLine("market_id,decision_count,order_event_count,order_count,trade_count,position_count,total_buy_notional,total_sell_notional,net_pnl,realized_pnl,unrealized_pnl,estimated_slippage,avg_decision_to_fill_latency_ms,stale_data_event_count,unhedged_exposure_notional,unhedged_exposure_seconds");
        foreach (var item in report.MarketBreakdown)
        {
            sb.AppendLine(string.Join(",",
                Csv(item.MarketId),
                item.DecisionCount,
                item.OrderEventCount,
                item.OrderCount,
                item.TradeCount,
                item.PositionCount,
                Number(item.TotalBuyNotional),
                Number(item.TotalSellNotional),
                Number(item.NetPnl),
                Number(item.RealizedPnl),
                Number(item.UnrealizedPnl),
                Number(item.EstimatedSlippage),
                Number(item.AverageDecisionToFillLatencyMs),
                item.StaleDataEventCount,
                Number(item.UnhedgedExposureNotional),
                Number(item.UnhedgedExposureSeconds)));
        }

        sb.AppendLine();
    }

    private static void AppendAttribution(StringBuilder sb, PaperRunReport report)
    {
        var attribution = report.Attribution;
        sb.AppendLine("table,attribution");
        sb.AppendLine("metric,value,source,evidence_ids,notes");
        sb.AppendLine(string.Join(",",
            Csv("realized_pnl"),
            Number(attribution.Pnl.RealizedPnl),
            Csv(attribution.Pnl.RealizedPnlSource),
            string.Empty,
            Csv(string.Join(" | ", attribution.Pnl.Notes))));
        sb.AppendLine(string.Join(",",
            Csv("unrealized_pnl"),
            Number(attribution.Pnl.UnrealizedPnl),
            Csv(attribution.Pnl.UnrealizedPnlSource),
            string.Empty,
            Csv(attribution.Pnl.MarkSource)));
        sb.AppendLine(string.Join(",",
            Csv("fees"),
            Number(attribution.Pnl.Fees),
            Csv(attribution.Pnl.FeeSource),
            string.Empty,
            string.Empty));
        sb.AppendLine(string.Join(",",
            Csv("estimated_slippage"),
            Number(attribution.Slippage.EstimatedSlippage),
            Csv(attribution.Slippage.Source),
            Csv(string.Join("|", attribution.Slippage.EvidenceIds)),
            Csv(string.Join(" | ", attribution.Slippage.Notes))));
        sb.AppendLine(string.Join(",",
            Csv("avg_decision_to_fill_latency_ms"),
            Number(attribution.Latency.AverageDecisionToFillLatencyMs),
            Csv("order_events_and_strategy_decisions"),
            Csv(string.Join("|", attribution.Latency.EvidenceIds)),
            Csv(string.Join(" | ", attribution.Latency.Notes))));
        sb.AppendLine(string.Join(",",
            Csv("stale_data_events"),
            attribution.StaleData.EventCount,
            Csv(attribution.StaleData.Source),
            Csv(string.Join("|", attribution.StaleData.EvidenceIds)),
            Csv(string.Join(" | ", attribution.StaleData.Notes))));
        sb.AppendLine(string.Join(",",
            Csv("strategy_totals_reconcile"),
            attribution.StrategyTotalsReconcile.ToString(CultureInfo.InvariantCulture),
            Csv("strategy_breakdown"),
            string.Empty,
            Csv(string.Join(" | ", attribution.ReconciliationNotes))));
        sb.AppendLine(string.Join(",",
            Csv("market_totals_reconcile"),
            attribution.MarketTotalsReconcile.ToString(CultureInfo.InvariantCulture),
            Csv("market_breakdown"),
            string.Empty,
            Csv(string.Join(" | ", attribution.ReconciliationNotes))));
        sb.AppendLine();
    }

    private static void AppendUnhedgedExposures(StringBuilder sb, PaperRunReport report)
    {
        sb.AppendLine("table,unhedged_exposures");
        sb.AppendLine("evidence_id,strategy_id,market_id,notional,duration_seconds,mitigation_outcome,started_at_utc,ended_at_utc");
        foreach (var item in report.Attribution.UnhedgedExposure.Exposures)
        {
            sb.AppendLine(string.Join(",",
                item.EvidenceId,
                Csv(item.StrategyId),
                Csv(item.MarketId),
                Number(item.Notional),
                Number(item.DurationSeconds),
                Csv(item.MitigationOutcome),
                Timestamp(item.StartedAtUtc),
                Timestamp(item.EndedAtUtc)));
        }

        sb.AppendLine();
    }

    private static void AppendRiskEvents(StringBuilder sb, PaperRunReport report)
    {
        sb.AppendLine("table,risk_events");
        sb.AppendLine("created_at_utc,risk_event_id,severity,code,strategy_id,message,context_json");
        foreach (var item in report.RiskEvents)
        {
            sb.AppendLine(string.Join(",",
                Timestamp(item.CreatedAtUtc),
                item.Id,
                Csv(item.Severity.ToString()),
                Csv(item.Code),
                Csv(item.StrategyId),
                Csv(item.Message),
                Csv(item.ContextJson)));
        }

        sb.AppendLine();
    }

    private static void AppendIncidents(StringBuilder sb, PaperRunReport report)
    {
        sb.AppendLine("table,incidents");
        sb.AppendLine("timestamp_utc,source,severity,code,strategy_id,market_id,evidence_id,message");
        foreach (var item in report.NotableIncidents)
        {
            sb.AppendLine(string.Join(",",
                Timestamp(item.TimestampUtc),
                Csv(item.Source),
                Csv(item.Severity),
                Csv(item.Code),
                Csv(item.StrategyId),
                Csv(item.MarketId),
                item.EvidenceId,
                Csv(item.Message)));
        }

        sb.AppendLine();
    }

    private static void AppendEvidence(StringBuilder sb, PaperRunReport report)
    {
        sb.AppendLine("table,evidence");
        sb.AppendLine("evidence_type,evidence_id");
        AppendIds(sb, "decision", report.EvidenceLinks.DecisionIds);
        AppendIds(sb, "order_event", report.EvidenceLinks.OrderEventIds);
        AppendIds(sb, "order", report.EvidenceLinks.OrderIds);
        AppendIds(sb, "trade", report.EvidenceLinks.TradeIds);
        AppendIds(sb, "position", report.EvidenceLinks.PositionIds);
        AppendIds(sb, "risk_event", report.EvidenceLinks.RiskEventIds);
    }

    private static void AppendIds(StringBuilder sb, string type, IReadOnlyList<Guid> ids)
    {
        foreach (var id in ids)
        {
            sb.AppendLine(string.Join(",", Csv(type), id));
        }
    }

    private static string Timestamp(DateTimeOffset? value)
        => value?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Number(decimal value)
        => value.ToString(CultureInfo.InvariantCulture);

    private static string Number(decimal? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Number(double? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var needsQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        return needsQuote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
