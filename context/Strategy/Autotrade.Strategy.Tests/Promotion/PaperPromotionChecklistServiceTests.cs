using Autotrade.Strategy.Application.Promotion;
using Autotrade.Strategy.Application.RunReports;
using Autotrade.Strategy.Application.RunSessions;
using Autotrade.Trading.Application.Contract.Accounts;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Options;

namespace Autotrade.Strategy.Tests.Promotion;

public sealed class PaperPromotionChecklistServiceTests
{
    [Fact]
    public async Task EvaluateAsync_WhenReportMeetsCriteria_ReturnsPassedAndLeavesLiveArmingUnchanged()
    {
        var sessionId = Guid.NewGuid();
        var report = CreateReport(sessionId, CreateStoppedSession(sessionId));
        var service = CreateService(report);

        var checklist = await service.EvaluateAsync(sessionId);

        Assert.NotNull(checklist);
        Assert.Equal("Passed", checklist.OverallStatus);
        Assert.True(checklist.CanConsiderLive);
        Assert.True(checklist.LiveArmingUnchanged);
        Assert.All(checklist.Criteria, criterion => Assert.Equal("Passed", criterion.Status));
        Assert.Empty(checklist.ResidualRisks);
    }

    [Fact]
    public async Task EvaluateAsync_WhenPromotionEvidenceFails_ReturnsReasonsAndAffectedEvidenceIds()
    {
        var sessionId = Guid.NewGuid();
        var staleRiskEventId = Guid.NewGuid();
        var blockingRiskEventId = Guid.NewGuid();
        var orderIncidentId = Guid.NewGuid();
        var activeSession = CreateActiveSession(sessionId);
        var report = CreateReport(
            sessionId,
            activeSession,
            summary: new PaperRunReportSummary(
                DecisionCount: 0,
                OrderEventCount: 1,
                OrderCount: 0,
                TradeCount: 0,
                PositionCount: 0,
                RiskEventCount: 2,
                FilledOrderEventCount: 0,
                RejectedOrderEventCount: 1,
                TotalBuyNotional: 0m,
                TotalSellNotional: 0m,
                TotalFees: 0m,
                GrossPnl: 0m,
                NetPnl: 0m),
            strategyBreakdown:
            [
                new PaperRunStrategyBreakdown(
                    "dual_leg_arbitrage",
                    DecisionCount: 0,
                    OrderEventCount: 1,
                    OrderCount: 0,
                    TradeCount: 0,
                    RiskEventCount: 2,
                    TotalBuyNotional: 0m,
                    TotalSellNotional: 0m,
                    TotalFees: 0m,
                    NetPnl: 0m)
            ],
            riskEvents:
            [
                new RiskEventRecord(
                    staleRiskEventId,
                    "MARKET_DATA_STALE",
                    RiskSeverity.Warning,
                    "Market data stale for market-1.",
                    "dual_leg_arbitrage",
                    "{}",
                    activeSession.StartedAtUtc.AddMinutes(1)),
                new RiskEventRecord(
                    blockingRiskEventId,
                    "RISK_KILL_SWITCH",
                    RiskSeverity.Critical,
                    "Kill switch activated.",
                    "dual_leg_arbitrage",
                    "{}",
                    activeSession.StartedAtUtc.AddMinutes(2))
            ],
            notableIncidents:
            [
                new PaperRunIncident(
                    activeSession.StartedAtUtc.AddMinutes(3),
                    "OrderEvent",
                    "Error",
                    "Rejected",
                    "Order rejected by risk manager.",
                    "dual_leg_arbitrage",
                    "market-1",
                    orderIncidentId)
            ]);
        var unhedged = new UnhedgedExposureSnapshot(
            "dual_leg_arbitrage",
            "market-1",
            "token-yes",
            "token-no",
            OutcomeSide.Yes,
            OrderSide.Buy,
            Quantity: 10m,
            Price: 0.45m,
            Notional: 4.5m,
            StartedAtUtc: activeSession.StartedAtUtc);
        var riskSnapshot = EmptyRiskSnapshot() with { UnhedgedExposures = [unhedged] };
        var service = CreateService(
            report,
            riskSnapshot,
            new StubAccountSyncService(lastSyncTime: null));

        var checklist = await service.EvaluateAsync(sessionId);

        Assert.NotNull(checklist);
        Assert.Equal("Failed", checklist.OverallStatus);
        Assert.False(checklist.CanConsiderLive);

        var criteria = checklist.Criteria.ToDictionary(criterion => criterion.Id, StringComparer.Ordinal);
        Assert.All(criteria.Values.Where(criterion => criterion.Status == "Failed"),
            criterion => Assert.False(string.IsNullOrWhiteSpace(criterion.Reason)));
        Assert.Contains(sessionId, criteria["run_duration"].EvidenceIds);
        Assert.Contains(staleRiskEventId, criteria["stale_data"].EvidenceIds);
        Assert.Contains(blockingRiskEventId, criteria["risk_events"].EvidenceIds);
        Assert.Contains(orderIncidentId, criteria["order_errors"].EvidenceIds);
        Assert.Equal("Failed", criteria["unhedged_exposure"].Status);
        Assert.Equal("Failed", criteria["reconciliation_health"].Status);
        Assert.Equal("Failed", criteria["pnl_attribution"].Status);
    }

    [Fact]
    public async Task EvaluateAsync_WhenOnlyWarningRiskEventsExist_PassesAndSurfacesResidualRisks()
    {
        var sessionId = Guid.NewGuid();
        var riskEventId = Guid.NewGuid();
        var session = CreateStoppedSession(sessionId);
        var report = CreateReport(
            sessionId,
            session,
            riskEvents:
            [
                new RiskEventRecord(
                    riskEventId,
                    "RISK_OPEN_ORDER_HEADROOM",
                    RiskSeverity.Warning,
                    "Open-order headroom was narrow.",
                    "dual_leg_arbitrage",
                    "{}",
                    session.StartedAtUtc.AddMinutes(10))
            ]);
        var service = CreateService(report);

        var checklist = await service.EvaluateAsync(sessionId);

        Assert.NotNull(checklist);
        Assert.Equal("Passed", checklist.OverallStatus);
        Assert.True(checklist.CanConsiderLive);
        Assert.Contains(checklist.ResidualRisks, risk => risk.Contains("RISK_OPEN_ORDER_HEADROOM", StringComparison.Ordinal));
        Assert.Contains(riskEventId, checklist.Criteria.Single(criterion => criterion.Id == "risk_events").EvidenceIds);
    }

    [Fact]
    public void CsvFormatter_IncludesCriteriaEvidenceAndResidualRisks()
    {
        var sessionId = Guid.NewGuid();
        var evidenceId = Guid.NewGuid();
        var checklist = new PaperPromotionChecklist(
            sessionId,
            DateTimeOffset.UtcNow,
            "Passed",
            CanConsiderLive: true,
            LiveArmingUnchanged: true,
            Criteria:
            [
                new PaperPromotionCriterion(
                    "risk_events",
                    "Risk events",
                    "Passed",
                    "Only warning-level risk events were found; review residual risks.",
                    [evidenceId],
                    ["RISK_OPEN_ORDER_HEADROOM: Open-order headroom was narrow."])
            ],
            ResidualRisks: ["RISK_OPEN_ORDER_HEADROOM: Open-order headroom was narrow."]);

        var csv = PaperPromotionChecklistCsvFormatter.Format(checklist);

        Assert.Contains("table,checklist", csv);
        Assert.Contains("table,criteria", csv);
        Assert.Contains(evidenceId.ToString(), csv);
        Assert.Contains("RISK_OPEN_ORDER_HEADROOM", csv);
    }

    private static PaperPromotionChecklistService CreateService(
        PaperRunReport? report,
        RiskStateSnapshot? riskSnapshot = null,
        IAccountSyncService? accountSyncService = null,
        PaperPromotionChecklistOptions? options = null)
        => new(
            new StubRunReportService(report),
            new StubRiskManager(riskSnapshot ?? EmptyRiskSnapshot()),
            accountSyncService ?? new StubAccountSyncService(DateTimeOffset.UtcNow),
            Options.Create(options ?? new PaperPromotionChecklistOptions
            {
                MinRunDurationMinutes = 30,
                MaxAccountSyncAgeSeconds = 300,
                MaxOrderErrorCount = 0,
                MaxUnhedgedExposures = 0,
                RequireStoppedSession = true,
                RequireTradesForPnlAttribution = true
            }));

    private static PaperRunReport CreateReport(
        Guid sessionId,
        PaperRunSessionRecord session,
        PaperRunReportSummary? summary = null,
        IReadOnlyList<PaperRunStrategyBreakdown>? strategyBreakdown = null,
        IReadOnlyList<PaperRunMarketBreakdown>? marketBreakdown = null,
        IReadOnlyList<RiskEventRecord>? riskEvents = null,
        IReadOnlyList<PaperRunIncident>? notableIncidents = null)
    {
        var decisionId = Guid.NewGuid();
        var orderEventId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var tradeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var effectiveSummary = summary ?? new PaperRunReportSummary(
            DecisionCount: 1,
            OrderEventCount: 2,
            OrderCount: 1,
            TradeCount: 1,
            PositionCount: 1,
            RiskEventCount: riskEvents?.Count ?? 0,
            FilledOrderEventCount: 1,
            RejectedOrderEventCount: 0,
            TotalBuyNotional: 10m,
            TotalSellNotional: 12m,
            TotalFees: 0.1m,
            GrossPnl: 2m,
            NetPnl: 1.9m);

        return new PaperRunReport(
            DateTimeOffset.UtcNow,
            "Complete",
            [],
            session,
            effectiveSummary,
            strategyBreakdown ??
            [
                new PaperRunStrategyBreakdown(
                    "dual_leg_arbitrage",
                    DecisionCount: 1,
                    OrderEventCount: 2,
                    OrderCount: 1,
                    TradeCount: 1,
                    RiskEventCount: riskEvents?.Count ?? 0,
                    TotalBuyNotional: 10m,
                    TotalSellNotional: 12m,
                    TotalFees: 0.1m,
                    NetPnl: 1.9m)
            ],
            marketBreakdown ??
            [
                new PaperRunMarketBreakdown(
                    "market-1",
                    DecisionCount: 1,
                    OrderEventCount: 2,
                    OrderCount: 1,
                    TradeCount: 1,
                    PositionCount: 1,
                    TotalBuyNotional: 10m,
                    TotalSellNotional: 12m,
                    NetPnl: effectiveSummary.NetPnl)
            ],
            riskEvents ?? [],
            notableIncidents ?? [],
            new PaperRunEvidenceLinks(
                [decisionId],
                [orderEventId],
                [orderId],
                [tradeId],
                [positionId],
                riskEvents?.Select(riskEvent => riskEvent.Id).ToArray() ?? []),
            new PaperRunExportReferences(
                $"/api/run-reports/{sessionId}",
                $"autotrade export run-report --session-id {sessionId} --json",
                $"autotrade export run-report --session-id {sessionId}",
                ["summary", "criteria"]));
    }

    private static PaperRunSessionRecord CreateStoppedSession(Guid sessionId)
    {
        var started = DateTimeOffset.UtcNow.AddHours(-2);
        return new PaperRunSessionRecord(
            sessionId,
            "Paper",
            "cfg-v1",
            ["dual_leg_arbitrage"],
            "{\"maxExposure\":100}",
            "test",
            started,
            started.AddHours(1),
            "completed",
            IsActive: false,
            Recovered: false);
    }

    private static PaperRunSessionRecord CreateActiveSession(Guid sessionId)
    {
        var started = DateTimeOffset.UtcNow.AddMinutes(-5);
        return new PaperRunSessionRecord(
            sessionId,
            "Paper",
            "cfg-v1",
            ["dual_leg_arbitrage"],
            "{\"maxExposure\":100}",
            "test",
            started,
            StoppedAtUtc: null,
            StopReason: null,
            IsActive: true,
            Recovered: false);
    }

    private static RiskStateSnapshot EmptyRiskSnapshot()
        => new(
            TotalOpenNotional: 0m,
            TotalOpenOrders: 0,
            TotalCapital: 100m,
            AvailableCapital: 100m,
            CapitalUtilizationPct: 0m,
            NotionalByStrategy: new Dictionary<string, decimal>(StringComparer.Ordinal),
            NotionalByMarket: new Dictionary<string, decimal>(StringComparer.Ordinal),
            OpenOrdersByStrategy: new Dictionary<string, int>(StringComparer.Ordinal),
            UnhedgedExposures: []);

    private sealed class StubRunReportService(PaperRunReport? report) : IPaperRunReportService
    {
        public Task<PaperRunReport?> GetAsync(
            Guid sessionId,
            int limit = 1000,
            CancellationToken cancellationToken = default)
            => Task.FromResult(report?.Session.SessionId == sessionId ? report : null);
    }

    private sealed class StubAccountSyncService(DateTimeOffset? lastSyncTime) : IAccountSyncService
    {
        public DateTimeOffset? LastSyncTime { get; } = lastSyncTime;

        public ExternalBalanceSnapshot? LastBalanceSnapshot =>
            LastSyncTime is null ? null : new ExternalBalanceSnapshot(100m, 100m, LastSyncTime.Value);

        public IReadOnlyList<ExternalPositionSnapshot>? LastPositionsSnapshot { get; } = [];

        public Task<BalanceSyncResult> SyncBalanceAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new BalanceSyncResult(true, 100m, 100m));

        public Task<PositionsSyncResult> SyncPositionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new PositionsSyncResult(true, 0, 0, []));

        public Task<OpenOrdersSyncResult> SyncOpenOrdersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new OpenOrdersSyncResult(true, 0, 0, 0));

        public Task<FullSyncResult> SyncAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new FullSyncResult(
                true,
                new BalanceSyncResult(true, 100m, 100m),
                new PositionsSyncResult(true, 0, 0, []),
                new OpenOrdersSyncResult(true, 0, 0, 0)));
    }

    private sealed class StubRiskManager(RiskStateSnapshot snapshot) : IRiskManager
    {
        public Task<RiskCheckResult> ValidateOrderAsync(RiskOrderRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(RiskCheckResult.Allow());

        public Task RecordOrderAcceptedAsync(RiskOrderRequest request, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordOrderUpdateAsync(RiskOrderUpdate update, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordOrderErrorAsync(
            string strategyId,
            string clientOrderId,
            string errorCode,
            string message,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ActivateKillSwitchAsync(string reason, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ActivateKillSwitchAsync(
            KillSwitchLevel level,
            string reasonCode,
            string reason,
            string? contextJson = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ActivateStrategyKillSwitchAsync(
            string strategyId,
            KillSwitchLevel level,
            string reasonCode,
            string reason,
            string? marketId = null,
            string? contextJson = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ResetKillSwitchAsync(string? strategyId = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public bool IsKillSwitchActive => false;

        public KillSwitchState GetKillSwitchState() => KillSwitchState.Inactive;

        public KillSwitchState GetStrategyKillSwitchState(string strategyId) => KillSwitchState.Inactive;

        public bool IsStrategyBlocked(string strategyId) => false;

        public IReadOnlyList<KillSwitchState> GetAllActiveKillSwitches() => [];

        public IReadOnlyList<string> GetOpenOrderIds() => [];

        public IReadOnlyList<string> GetOpenOrderIds(string strategyId) => [];

        public IReadOnlyList<UnhedgedExposureSnapshot> GetExpiredUnhedgedExposures(DateTimeOffset nowUtc) => [];

        public Task RecordUnhedgedExposureAsync(
            string strategyId,
            string marketId,
            string tokenId,
            string hedgeTokenId,
            OutcomeSide outcome,
            OrderSide side,
            decimal quantity,
            decimal price,
            DateTimeOffset startedAtUtc,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ClearUnhedgedExposureAsync(
            string strategyId,
            string marketId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public RiskStateSnapshot GetStateSnapshot() => snapshot;
    }
}
