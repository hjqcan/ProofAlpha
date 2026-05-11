using Autotrade.Strategy.Application.RunReports;
using Autotrade.Trading.Application.Contract.Accounts;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Options;

namespace Autotrade.Strategy.Application.Promotion;

public sealed class PaperPromotionChecklistService(
    IPaperRunReportService reportService,
    IRiskManager riskManager,
    IAccountSyncService accountSyncService,
    IOptions<PaperPromotionChecklistOptions> options) : IPaperPromotionChecklistService
{
    private readonly PaperPromotionChecklistOptions _options =
        options?.Value ?? throw new ArgumentNullException(nameof(options));

    public async Task<PaperPromotionChecklist?> EvaluateAsync(
        Guid sessionId,
        int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        var report = await reportService.GetAsync(sessionId, limit, cancellationToken).ConfigureAwait(false);
        if (report is null)
        {
            return null;
        }

        var criteria = new[]
        {
            EvaluateRunDuration(report),
            EvaluateDecisionCoverage(report),
            EvaluateStaleData(report),
            EvaluateRiskEvents(report),
            EvaluateUnhedgedExposure(),
            EvaluateOrderErrors(report),
            EvaluateReconciliationHealth(),
            EvaluatePnlAttribution(report)
        };

        var residualRisks = criteria
            .SelectMany(criterion => criterion.ResidualRisks)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var canConsiderLive = criteria.All(criterion => criterion.Status == "Passed");

        return new PaperPromotionChecklist(
            report.Session.SessionId,
            DateTimeOffset.UtcNow,
            canConsiderLive ? "Passed" : "Failed",
            canConsiderLive,
            LiveArmingUnchanged: true,
            criteria,
            residualRisks);
    }

    private PaperPromotionCriterion EvaluateRunDuration(PaperRunReport report)
    {
        var duration = (report.Session.StoppedAtUtc ?? report.GeneratedAtUtc) - report.Session.StartedAtUtc;
        var evidence = new[] { report.Session.SessionId };
        if (_options.RequireStoppedSession && report.Session.IsActive)
        {
            return Fail(
                "run_duration",
                "Run duration",
                "Paper run is still active; stop the run before promotion review.",
                evidence);
        }

        if (duration < TimeSpan.FromMinutes(Math.Max(1, _options.MinRunDurationMinutes)))
        {
            return Fail(
                "run_duration",
                "Run duration",
                $"Paper run duration {duration.TotalMinutes:0.#}m is below required {_options.MinRunDurationMinutes}m.",
                evidence);
        }

        return Pass("run_duration", "Run duration", $"Paper run duration {duration.TotalMinutes:0.#}m meets threshold.", evidence);
    }

    private static PaperPromotionCriterion EvaluateDecisionCoverage(PaperRunReport report)
    {
        var missing = report.StrategyBreakdown
            .Where(strategy => strategy.DecisionCount == 0)
            .Select(strategy => strategy.StrategyId)
            .ToArray();

        if (report.Summary.DecisionCount == 0 || missing.Length > 0)
        {
            var reason = missing.Length == 0
                ? "No strategy decisions were recorded."
                : "Strategies without decisions: " + string.Join(", ", missing);
            return Fail("decision_coverage", "Decision coverage", reason, report.EvidenceLinks.DecisionIds);
        }

        return Pass(
            "decision_coverage",
            "Decision coverage",
            "Every reported strategy has decision evidence.",
            report.EvidenceLinks.DecisionIds);
    }

    private static PaperPromotionCriterion EvaluateStaleData(PaperRunReport report)
    {
        var staleEvents = report.RiskEvents
            .Where(riskEvent => ContainsIgnoreCase(riskEvent.Code, "STALE")
                || ContainsIgnoreCase(riskEvent.Message, "STALE"))
            .ToArray();

        if (staleEvents.Length > 0)
        {
            return Fail(
                "stale_data",
                "Stale data",
                $"Found {staleEvents.Length} stale-data risk event(s).",
                staleEvents.Select(riskEvent => riskEvent.Id).ToArray());
        }

        return Pass("stale_data", "Stale data", "No stale-data risk events were found.", []);
    }

    private static PaperPromotionCriterion EvaluateRiskEvents(PaperRunReport report)
    {
        var blocking = report.RiskEvents
            .Where(riskEvent => riskEvent.Severity is RiskSeverity.Error or RiskSeverity.Critical)
            .ToArray();

        if (blocking.Length > 0)
        {
            return Fail(
                "risk_events",
                "Risk events",
                $"Found {blocking.Length} error or critical risk event(s).",
                blocking.Select(riskEvent => riskEvent.Id).ToArray());
        }

        var warnings = report.RiskEvents
            .Where(riskEvent => riskEvent.Severity == RiskSeverity.Warning)
            .Select(riskEvent => $"{riskEvent.Code}: {riskEvent.Message}")
            .ToArray();

        return new PaperPromotionCriterion(
            "risk_events",
            "Risk events",
            "Passed",
            warnings.Length == 0
                ? "No warning/error/critical risk events were found."
                : "Only warning-level risk events were found; review residual risks.",
            report.RiskEvents.Select(riskEvent => riskEvent.Id).ToArray(),
            warnings);
    }

    private PaperPromotionCriterion EvaluateUnhedgedExposure()
    {
        var snapshot = riskManager.GetStateSnapshot();
        var count = snapshot.UnhedgedExposures.Count;
        if (count > _options.MaxUnhedgedExposures)
        {
            return Fail(
                "unhedged_exposure",
                "Unhedged exposure",
                $"Current unhedged exposure count {count} exceeds allowed {_options.MaxUnhedgedExposures}.",
                []);
        }

        return Pass("unhedged_exposure", "Unhedged exposure", $"Current unhedged exposure count is {count}.", []);
    }

    private PaperPromotionCriterion EvaluateOrderErrors(PaperRunReport report)
    {
        var errorIncidents = report.NotableIncidents
            .Where(incident => incident.Source == "OrderEvent")
            .ToArray();
        var rejected = report.Summary.RejectedOrderEventCount;
        if (rejected > _options.MaxOrderErrorCount || errorIncidents.Length > 0)
        {
            return Fail(
                "order_errors",
                "Order errors",
                $"Rejected order events={rejected}, notable order incidents={errorIncidents.Length}.",
                errorIncidents.Select(incident => incident.EvidenceId).ToArray());
        }

        return Pass("order_errors", "Order errors", "No rejected/cancelled/expired order incidents were found.", []);
    }

    private PaperPromotionCriterion EvaluateReconciliationHealth()
    {
        var lastSync = accountSyncService.LastSyncTime;
        if (lastSync is null)
        {
            return Fail("reconciliation_health", "Reconciliation health", "Account sync has not completed.", []);
        }

        var maxAge = TimeSpan.FromSeconds(Math.Max(1, _options.MaxAccountSyncAgeSeconds));
        var age = DateTimeOffset.UtcNow - lastSync.Value;
        if (age > maxAge)
        {
            return Fail(
                "reconciliation_health",
                "Reconciliation health",
                $"Account sync is stale. Age={age.TotalSeconds:0}s, max={maxAge.TotalSeconds:0}s.",
                []);
        }

        return Pass("reconciliation_health", "Reconciliation health", $"Last account sync age is {age.TotalSeconds:0}s.", []);
    }

    private PaperPromotionCriterion EvaluatePnlAttribution(PaperRunReport report)
    {
        if (_options.RequireTradesForPnlAttribution && report.Summary.TradeCount == 0)
        {
            return Fail("pnl_attribution", "PnL attribution", "No trades were linked, so PnL attribution is not meaningful.", []);
        }

        var marketNet = report.MarketBreakdown.Sum(market => market.NetPnl);
        if (report.Summary.TradeCount > 0 && marketNet != report.Summary.NetPnl)
        {
            return Fail(
                "pnl_attribution",
                "PnL attribution",
                $"Market net PnL {marketNet} does not match summary net PnL {report.Summary.NetPnl}.",
                report.EvidenceLinks.TradeIds);
        }

        return Pass("pnl_attribution", "PnL attribution", "PnL is attributed across market breakdown.", report.EvidenceLinks.TradeIds);
    }

    private static PaperPromotionCriterion Pass(
        string id,
        string name,
        string reason,
        IReadOnlyList<Guid> evidenceIds)
        => new(id, name, "Passed", reason, evidenceIds, []);

    private static PaperPromotionCriterion Fail(
        string id,
        string name,
        string reason,
        IReadOnlyList<Guid> evidenceIds)
        => new(id, name, "Failed", reason, evidenceIds, []);

    private static bool ContainsIgnoreCase(string value, string token)
        => value.Contains(token, StringComparison.OrdinalIgnoreCase);
}
