using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.Strategy.Application.Promotion;
using Autotrade.Strategy.Application.RunReports;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Autotrade.OpportunityDiscovery.Infra.CrossCutting.IoC;

public sealed class StrategyPaperValidationSource : IOpportunityPaperValidationSource
{
    private readonly IServiceProvider _serviceProvider;

    public StrategyPaperValidationSource(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public async Task<OpportunityPaperValidationSnapshot?> GetAsync(
        Guid sessionId,
        int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        var reportService = _serviceProvider.GetService<IPaperRunReportService>();
        var checklistService = _serviceProvider.GetService<IPaperPromotionChecklistService>();
        if (reportService is null || checklistService is null)
        {
            return null;
        }

        var resolvedLimit = Math.Clamp(limit, 1, 10000);
        var reportTask = reportService.GetAsync(sessionId, resolvedLimit, cancellationToken);
        var checklistTask = checklistService.EvaluateAsync(sessionId, resolvedLimit, cancellationToken);
        await Task.WhenAll(reportTask, checklistTask).ConfigureAwait(false);

        var report = await reportTask.ConfigureAwait(false);
        var checklist = await checklistTask.ConfigureAwait(false);
        if (report is null || checklist is null)
        {
            return null;
        }

        var evidenceIds = report.EvidenceLinks.DecisionIds
            .Concat(report.EvidenceLinks.OrderEventIds)
            .Concat(report.EvidenceLinks.OrderIds)
            .Concat(report.EvidenceLinks.TradeIds)
            .Concat(report.EvidenceLinks.PositionIds)
            .Concat(report.EvidenceLinks.RiskEventIds)
            .Concat(checklist.Criteria.SelectMany(criterion => criterion.EvidenceIds))
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        var observationDays = Math.Max(
            0,
            (int)Math.Floor(((report.Session.StoppedAtUtc ?? report.GeneratedAtUtc) - report.Session.StartedAtUtc).TotalDays));

        return new OpportunityPaperValidationSnapshot(
            checklist.SessionId,
            checklist.GeneratedAtUtc,
            checklist.OverallStatus,
            checklist.CanConsiderLive,
            checklist.LiveArmingUnchanged,
            report.Summary.DecisionCount,
            report.Summary.TradeCount,
            report.Summary.RiskEventCount,
            report.RiskEvents.Count(riskEvent => riskEvent.Severity == RiskSeverity.Critical),
            observationDays,
            report.Summary.NetPnl,
            report.Attribution.Slippage.AdverseSlippage,
            evidenceIds,
            checklist.Criteria
                .Select(criterion => new OpportunityPaperCriterionSnapshot(
                    criterion.Id,
                    criterion.Name,
                    criterion.Status,
                    criterion.Reason,
                    criterion.EvidenceIds,
                    criterion.ResidualRisks))
                .ToArray(),
            checklist.ResidualRisks);
    }
}
