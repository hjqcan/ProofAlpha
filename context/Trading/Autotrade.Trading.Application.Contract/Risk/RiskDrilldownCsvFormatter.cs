using System.Globalization;
using System.Text;

namespace Autotrade.Trading.Application.Contract.Risk;

public static class RiskDrilldownCsvFormatter
{
    public static string FormatRiskEvents(IReadOnlyList<RiskEventDrilldown> drilldowns)
    {
        ArgumentNullException.ThrowIfNull(drilldowns);

        var builder = new StringBuilder();
        AppendRow(builder,
            "table",
            "risk_event_id",
            "code",
            "severity",
            "strategy_id",
            "market_id",
            "limit_name",
            "current_value",
            "threshold",
            "unit",
            "state",
            "selected_action",
            "mitigation_result",
            "affected_orders",
            "exposure_notional",
            "exposure_duration_seconds",
            "kill_switch_scope");

        foreach (var item in drilldowns)
        {
            AppendRow(builder,
                "risk_events",
                item.Event.Id.ToString(),
                item.Event.Code,
                item.Event.Severity.ToString(),
                item.Event.StrategyId ?? string.Empty,
                item.Event.MarketId ?? string.Empty,
                item.Trigger.LimitName ?? string.Empty,
                FormatDecimal(item.Trigger.CurrentValue),
                FormatDecimal(item.Trigger.Threshold),
                item.Trigger.Unit ?? string.Empty,
                item.Trigger.State,
                item.Action.SelectedAction,
                item.Action.MitigationResult ?? string.Empty,
                string.Join('|', item.AffectedOrders.Select(order => order.ClientOrderId ?? order.OrderId?.ToString() ?? string.Empty)),
                FormatDecimal(item.Exposure?.Notional),
                FormatDouble(item.Exposure?.DurationSeconds),
                item.KillSwitch?.Scope ?? string.Empty);
        }

        return builder.ToString();
    }

    public static string FormatUnhedgedExposures(IReadOnlyList<UnhedgedExposureDrilldown> exposures)
    {
        ArgumentNullException.ThrowIfNull(exposures);

        var builder = new StringBuilder();
        AppendRow(builder,
            "table",
            "evidence_id",
            "strategy_id",
            "market_id",
            "token_id",
            "hedge_token_id",
            "outcome",
            "side",
            "quantity",
            "price",
            "notional",
            "duration_seconds",
            "started_at_utc",
            "ended_at_utc",
            "timeout_seconds",
            "hedge_state",
            "mitigation_result",
            "source");

        foreach (var exposure in exposures)
        {
            AppendRow(builder,
                "unhedged_exposures",
                exposure.EvidenceId?.ToString() ?? string.Empty,
                exposure.StrategyId,
                exposure.MarketId,
                exposure.TokenId,
                exposure.HedgeTokenId,
                exposure.Outcome,
                exposure.Side,
                FormatDecimal(exposure.Quantity),
                FormatDecimal(exposure.Price),
                FormatDecimal(exposure.Notional),
                FormatDouble(exposure.DurationSeconds),
                exposure.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                exposure.EndedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
                FormatDouble(exposure.TimeoutSeconds),
                exposure.HedgeState,
                exposure.MitigationResult,
                exposure.Source);
        }

        return builder.ToString();
    }

    private static string FormatDecimal(decimal? value)
        => value?.ToString("0.##########", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string FormatDouble(double? value)
        => value?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty;

    private static void AppendRow(StringBuilder builder, params string[] values)
        => builder.AppendLine(string.Join(',', values.Select(Escape)));

    private static string Escape(string value)
    {
        if (value.Contains('"', StringComparison.Ordinal)
            || value.Contains(',', StringComparison.Ordinal)
            || value.Contains('\n', StringComparison.Ordinal)
            || value.Contains('\r', StringComparison.Ordinal))
        {
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        return value;
    }
}
