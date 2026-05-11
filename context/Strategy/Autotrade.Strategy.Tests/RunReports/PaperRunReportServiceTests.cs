using Autotrade.Application.DTOs;
using Autotrade.Application.RunSessions;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Decisions;
using Autotrade.Strategy.Application.RunReports;
using Autotrade.Strategy.Application.RunSessions;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Strategy.Tests.RunReports;

public sealed class PaperRunReportServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsCompleteReportWithBreakdownsIncidentsAndEvidence()
    {
        var sessionId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var tradeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var riskEventId = Guid.NewGuid();
        var session = CreateSession(sessionId, isActive: false);
        var decisions = new[]
        {
            new StrategyDecisionRecord(
                Guid.NewGuid(),
                "dual_leg_arbitrage",
                "Buy",
                "edge",
                "market-1",
                "{}",
                session.StartedAtUtc.AddMinutes(1),
                "cfg-v1",
                RunSessionId: sessionId)
        };
        var orderEvents = new[]
        {
            new OrderEventDto(
                Guid.NewGuid(),
                orderId,
                "client-1",
                "dual_leg_arbitrage",
                "market-1",
                OrderEventType.Accepted,
                OrderStatus.Open,
                "accepted",
                null,
                "corr-1",
                session.StartedAtUtc.AddMinutes(2),
                sessionId),
            new OrderEventDto(
                Guid.NewGuid(),
                orderId,
                "client-1",
                "dual_leg_arbitrage",
                "market-1",
                OrderEventType.Filled,
                OrderStatus.Filled,
                "filled",
                null,
                "corr-1",
                session.StartedAtUtc.AddMinutes(3),
                sessionId),
            new OrderEventDto(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "client-2",
                "dual_leg_arbitrage",
                "market-1",
                OrderEventType.Rejected,
                OrderStatus.Rejected,
                "risk rejected",
                null,
                "corr-2",
                session.StartedAtUtc.AddMinutes(4),
                sessionId)
        };
        var order = CreateOrder(orderId, "client-1");
        var trade = CreateTrade(tradeId, orderId, "client-1");
        var position = new PositionDto(
            positionId,
            Guid.NewGuid(),
            "market-1",
            OutcomeSide.Yes,
            10m,
            0.45m,
            1.2m,
            session.StartedAtUtc.AddMinutes(5));
        var riskEvent = new RiskEventRecord(
            riskEventId,
            "RISK_MAX_OPEN_ORDERS",
            RiskSeverity.Warning,
            "too many orders",
            "dual_leg_arbitrage",
            "{}",
            session.StartedAtUtc.AddMinutes(6));
        var service = CreateService(
            session,
            decisions,
            orderEvents,
            [order],
            [trade],
            [position],
            [riskEvent]);

        var report = await service.GetAsync(sessionId);

        Assert.NotNull(report);
        Assert.Equal("Complete", report.ReportStatus);
        Assert.Equal(1, report.Summary.DecisionCount);
        Assert.Equal(3, report.Summary.OrderEventCount);
        Assert.Equal(1, report.Summary.OrderCount);
        Assert.Equal(1, report.Summary.TradeCount);
        Assert.Equal(1, report.Summary.PositionCount);
        Assert.Equal(1, report.Summary.RiskEventCount);
        Assert.Equal(4.5m, report.Summary.TotalBuyNotional);
        Assert.Equal(-4.51m, report.Summary.NetPnl);
        Assert.Contains(report.EvidenceLinks.DecisionIds, id => id == decisions[0].DecisionId);
        Assert.Contains(report.EvidenceLinks.OrderEventIds, id => id == orderEvents[0].Id);
        Assert.Contains(report.EvidenceLinks.OrderIds, id => id == orderId);
        Assert.Contains(report.EvidenceLinks.TradeIds, id => id == tradeId);
        Assert.Contains(report.EvidenceLinks.PositionIds, id => id == positionId);
        Assert.Contains(report.EvidenceLinks.RiskEventIds, id => id == riskEventId);
        Assert.Single(report.StrategyBreakdown);
        Assert.Single(report.MarketBreakdown);
        Assert.Contains(report.NotableIncidents, incident => incident.Source == "RiskEvent");
        Assert.Contains(report.NotableIncidents, incident => incident.Source == "OrderEvent");
        Assert.Equal($"/api/run-reports/{sessionId}", report.ExportReferences.JsonApi);

        var csv = PaperRunReportCsvFormatter.Format(report);
        Assert.Contains("table,summary", csv);
        Assert.Contains("table,evidence", csv);
        Assert.Contains(tradeId.ToString(), csv);
        Assert.Contains(riskEventId.ToString(), csv);
    }

    [Fact]
    public async Task GetAsync_ActiveRunWithoutEvidenceReturnsEmptyStatusAndExplicitNotes()
    {
        var sessionId = Guid.NewGuid();
        var session = CreateSession(sessionId, isActive: true);
        var service = CreateService(
            session,
            [],
            [],
            [],
            [],
            [],
            []);

        var report = await service.GetAsync(sessionId);

        Assert.NotNull(report);
        Assert.Equal("Empty", report.ReportStatus);
        Assert.Contains(report.CompletenessNotes, note => note.Contains("still active", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.CompletenessNotes, note => note.Contains("No decisions", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(report.EvidenceLinks.DecisionIds);
    }

    [Fact]
    public async Task GetAsync_ReturnsAttributionForPnlSlippageLatencyStaleDataAndUnhedgedExposure()
    {
        var sessionId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var tradeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var staleRiskEventId = Guid.NewGuid();
        var unhedgedRiskEventId = Guid.NewGuid();
        var session = CreateSession(sessionId, isActive: false);
        var decision = new StrategyDecisionRecord(
            Guid.NewGuid(),
            "dual_leg_arbitrage",
            "Buy",
            "edge",
            "market-1",
            "{}",
            session.StartedAtUtc.AddMinutes(1),
            "cfg-v1",
            RunSessionId: sessionId);
        var acceptedEvent = new OrderEventDto(
            Guid.NewGuid(),
            orderId,
            "client-1",
            "dual_leg_arbitrage",
            "market-1",
            OrderEventType.Accepted,
            OrderStatus.Open,
            "accepted",
            null,
            "corr-1",
            session.StartedAtUtc.AddMinutes(2),
            sessionId);
        var filledEvent = new OrderEventDto(
            Guid.NewGuid(),
            orderId,
            "client-1",
            "dual_leg_arbitrage",
            "market-1",
            OrderEventType.Filled,
            OrderStatus.Filled,
            "filled",
            null,
            "corr-1",
            session.StartedAtUtc.AddMinutes(3),
            sessionId);
        var unhedgedStarted = session.StartedAtUtc.AddMinutes(4);
        var unhedgedEnded = unhedgedStarted.AddSeconds(120);
        var unhedgedContextJson = "{" +
            "\"StrategyId\":\"dual_leg_arbitrage\"," +
            "\"MarketId\":\"market-1\"," +
            "\"TokenId\":\"token-yes\"," +
            "\"HedgeTokenId\":\"token-no\"," +
            "\"Notional\":4.5," +
            "\"StartedAtUtc\":\"" + unhedgedStarted.ToString("O") + "\"," +
            "\"ExpiredAtUtc\":\"" + unhedgedEnded.ToString("O") + "\"," +
            "\"ExposureDurationSeconds\":120," +
            "\"ExitAction\":\"ForceHedge\"" +
            "}";
        var service = CreateService(
            session,
            [decision],
            [acceptedEvent, filledEvent],
            [CreateOrder(orderId, "client-1", price: 0.40m)],
            [CreateTrade(tradeId, orderId, "client-1", price: 0.45m)],
            [new PositionDto(
                positionId,
                Guid.NewGuid(),
                "market-1",
                OutcomeSide.Yes,
                10m,
                0.45m,
                -0.25m,
                session.StartedAtUtc.AddMinutes(5))],
            [
                new RiskEventRecord(
                    staleRiskEventId,
                    "MARKET_DATA_STALE",
                    RiskSeverity.Warning,
                    "Market data stale for market-1.",
                    "dual_leg_arbitrage",
                    "{\"MarketId\":\"market-1\"}",
                    session.StartedAtUtc.AddMinutes(4)),
                new RiskEventRecord(
                    unhedgedRiskEventId,
                    "RISK_UNHEDGED_TIMEOUT_FORCE_HEDGE",
                    RiskSeverity.Critical,
                    "Unhedged exposure timeout, force hedge executed: market-1",
                    "dual_leg_arbitrage",
                    unhedgedContextJson,
                    unhedgedEnded)
            ]);

        var report = await service.GetAsync(sessionId);

        Assert.NotNull(report);
        Assert.Equal(-0.25m, report.Attribution.Pnl.RealizedPnl);
        Assert.Null(report.Attribution.Pnl.UnrealizedPnl);
        Assert.Equal("position_realized_pnl", report.Attribution.Pnl.RealizedPnlSource);
        Assert.Equal(0.5m, report.Attribution.Slippage.EstimatedSlippage);
        Assert.Equal(1, report.Attribution.Slippage.TradeCountWithEstimate);
        Assert.Equal(120000d, report.Attribution.Latency.AverageDecisionToFillLatencyMs);
        Assert.Equal(60000d, report.Attribution.Latency.AverageAcceptedToFillLatencyMs);
        Assert.Equal(1, report.Attribution.StaleData.EventCount);
        Assert.Contains(staleRiskEventId, report.Attribution.StaleData.EvidenceIds);
        var unhedged = Assert.Single(report.Attribution.UnhedgedExposure.Exposures);
        Assert.Equal(unhedgedRiskEventId, unhedged.EvidenceId);
        Assert.Equal(4.5m, unhedged.Notional);
        Assert.Equal(120d, unhedged.DurationSeconds);
        Assert.Equal("ForceHedge", unhedged.MitigationOutcome);
        Assert.True(report.Attribution.StrategyTotalsReconcile);
        Assert.True(report.Attribution.MarketTotalsReconcile);

        var strategy = Assert.Single(report.StrategyBreakdown);
        Assert.Equal(0.5m, strategy.EstimatedSlippage);
        Assert.Equal(120000d, strategy.AverageDecisionToFillLatencyMs);
        Assert.Equal(1, strategy.StaleDataEventCount);
        Assert.Equal(4.5m, strategy.UnhedgedExposureNotional);
        Assert.Equal(120d, strategy.UnhedgedExposureSeconds);

        var market = Assert.Single(report.MarketBreakdown);
        Assert.Equal(0.5m, market.EstimatedSlippage);
        Assert.Equal(120000d, market.AverageDecisionToFillLatencyMs);
        Assert.Equal(1, market.StaleDataEventCount);
        Assert.Equal(4.5m, market.UnhedgedExposureNotional);
        Assert.Equal(120d, market.UnhedgedExposureSeconds);

        var csv = PaperRunReportCsvFormatter.Format(report);
        Assert.Contains("table,attribution", csv);
        Assert.Contains("table,unhedged_exposures", csv);
        Assert.Contains(unhedgedRiskEventId.ToString(), csv);
    }

    private static PaperRunReportService CreateService(
        PaperRunSessionRecord session,
        IReadOnlyList<StrategyDecisionRecord> decisions,
        IReadOnlyList<OrderEventDto> orderEvents,
        IReadOnlyList<OrderDto> orders,
        IReadOnlyList<TradeDto> trades,
        IReadOnlyList<PositionDto> positions,
        IReadOnlyList<RiskEventRecord> riskEvents)
        => new(
            new StubPaperRunSessionService(session),
            new StubDecisionQueryService(decisions),
            new StubOrderEventRepository(orderEvents),
            new StubOrderRepository(orders),
            new StubTradeRepository(trades),
            new StubPositionRepository(positions),
            new StubRiskEventRepository(riskEvents));

    private static PaperRunSessionRecord CreateSession(Guid sessionId, bool isActive)
    {
        var started = new DateTimeOffset(2026, 5, 3, 12, 0, 0, TimeSpan.Zero);
        var stopped = isActive ? (DateTimeOffset?)null : started.AddHours(1);
        return new PaperRunSessionRecord(
            sessionId,
            "Paper",
            "cfg-v1",
            ["dual_leg_arbitrage"],
            "{\"maxExposure\":100}",
            "test",
            started,
            stopped,
            isActive ? null : "done",
            isActive,
            false);
    }

    private static OrderDto CreateOrder(Guid orderId, string clientOrderId, decimal price = 0.45m)
        => new(
            orderId,
            Guid.NewGuid(),
            "market-1",
            "token-yes",
            "dual_leg_arbitrage",
            clientOrderId,
            "exchange-1",
            "corr-1",
            OutcomeSide.Yes,
            OrderSide.Buy,
            OrderType.Limit,
            TimeInForce.Gtc,
            null,
            false,
            price,
            10m,
            10m,
            OrderStatus.Filled,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

    private static TradeDto CreateTrade(Guid tradeId, Guid orderId, string clientOrderId, decimal price = 0.45m)
        => new(
            tradeId,
            orderId,
            Guid.NewGuid(),
            clientOrderId,
            "dual_leg_arbitrage",
            "market-1",
            "token-yes",
            OutcomeSide.Yes,
            OrderSide.Buy,
            price,
            10m,
            "trade-1",
            0.01m,
            "corr-1",
            DateTimeOffset.UtcNow);

    private sealed class StubPaperRunSessionService(PaperRunSessionRecord session) : IPaperRunSessionService
    {
        public Task<PaperRunSessionRecord> StartOrRecoverAsync(
            PaperRunSessionStartRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(session);

        public Task<PaperRunSessionRecord?> StopAsync(
            PaperRunSessionStopRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult<PaperRunSessionRecord?>(session);

        public Task<PaperRunSessionRecord?> GetActiveAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(session.IsActive ? session : null);

        public Task<PaperRunSessionRecord?> ExportAsync(Guid sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(sessionId == session.SessionId ? session : null);

        public Task<RunSessionIdentity?> GetCurrentAsync(string executionMode, CancellationToken cancellationToken = default)
            => Task.FromResult<RunSessionIdentity?>(new RunSessionIdentity(
                session.SessionId,
                session.ExecutionMode,
                session.ConfigVersion,
                session.StartedAtUtc));
    }

    private sealed class StubDecisionQueryService(IReadOnlyList<StrategyDecisionRecord> decisions) : IStrategyDecisionQueryService
    {
        public Task<IReadOnlyList<StrategyDecision>> QueryAsync(
            StrategyDecisionQuery query,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<StrategyDecision>>([]);

        public Task<IReadOnlyList<StrategyDecisionRecord>> QueryRecordsAsync(
            StrategyDecisionQuery query,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<StrategyDecisionRecord>>(decisions
                .Where(decision => !query.RunSessionId.HasValue || decision.RunSessionId == query.RunSessionId)
                .ToArray());

        public Task<StrategyDecisionRecord?> GetAsync(Guid decisionId, CancellationToken cancellationToken = default)
            => Task.FromResult(decisions.FirstOrDefault(decision => decision.DecisionId == decisionId));
    }

    private sealed class StubOrderEventRepository(IReadOnlyList<OrderEventDto> events) : IOrderEventRepository
    {
        public Task<IReadOnlyList<OrderEventDto>> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OrderEventDto>>(events.Where(item => item.OrderId == orderId).ToArray());

        public Task<IReadOnlyList<OrderEventDto>> GetByClientOrderIdAsync(string clientOrderId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OrderEventDto>>(events.Where(item => item.ClientOrderId == clientOrderId).ToArray());

        public Task<IReadOnlyList<OrderEventDto>> GetByRunSessionIdAsync(
            Guid runSessionId,
            int? limit = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OrderEventDto>>(events
                .Where(item => item.RunSessionId == runSessionId)
                .Take(limit ?? int.MaxValue)
                .ToArray());

        public Task<IReadOnlyList<OrderEventDto>> GetByStrategyIdAsync(
            string strategyId,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            int? limit = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OrderEventDto>>([]);

        public Task<PagedResultDto<OrderEventDto>> GetPagedAsync(
            int page,
            int pageSize,
            string? strategyId = null,
            string? marketId = null,
            OrderEventType? eventType = null,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new PagedResultDto<OrderEventDto>([], 0, page, pageSize));

        public Task AddAsync(OrderEventDto orderEvent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task AddRangeAsync(IEnumerable<OrderEventDto> orderEvents, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<int> DeleteBeforeAsync(DateTimeOffset beforeUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private sealed class StubOrderRepository(IReadOnlyList<OrderDto> orders) : IOrderRepository
    {
        public Task AddAsync(OrderDto order, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddRangeAsync(IEnumerable<OrderDto> orders, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(OrderDto order, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<OrderDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(orders.FirstOrDefault(order => order.Id == id));
        public Task<OrderDto?> GetByClientOrderIdAsync(string clientOrderId, CancellationToken cancellationToken = default)
            => Task.FromResult(orders.FirstOrDefault(order => order.ClientOrderId == clientOrderId));
        public Task<OrderDto?> GetByExchangeOrderIdAsync(string exchangeOrderId, CancellationToken cancellationToken = default)
            => Task.FromResult(orders.FirstOrDefault(order => order.ExchangeOrderId == exchangeOrderId));
        public Task<IReadOnlyList<OrderDto>> GetOpenOrdersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OrderDto>>([]);
        public Task<IReadOnlyList<OrderDto>> GetByStrategyIdAsync(string strategyId, DateTimeOffset? from = null, DateTimeOffset? to = null, int? limit = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OrderDto>>([]);
        public Task<IReadOnlyList<OrderDto>> GetByMarketIdAsync(string marketId, DateTimeOffset? from = null, DateTimeOffset? to = null, int? limit = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OrderDto>>([]);
        public Task<IReadOnlyList<OrderDto>> GetByStatusAsync(OrderStatus status, DateTimeOffset? from = null, DateTimeOffset? to = null, int? limit = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OrderDto>>([]);
        public Task<PagedResultDto<OrderDto>> GetPagedAsync(int page, int pageSize, string? strategyId = null, string? marketId = null, OrderStatus? status = null, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new PagedResultDto<OrderDto>([], 0, page, pageSize));
        public Task<int> DeleteBeforeAsync(DateTimeOffset beforeUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private sealed class StubTradeRepository(IReadOnlyList<TradeDto> trades) : ITradeRepository
    {
        public Task<TradeDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(trades.FirstOrDefault(trade => trade.Id == id));
        public Task<IReadOnlyList<TradeDto>> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TradeDto>>(trades.Where(trade => trade.OrderId == orderId).ToArray());
        public Task<IReadOnlyList<TradeDto>> GetByClientOrderIdAsync(string clientOrderId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TradeDto>>([]);
        public Task<TradeDto?> GetByExchangeTradeIdAsync(string exchangeTradeId, CancellationToken cancellationToken = default)
            => Task.FromResult<TradeDto?>(null);
        public Task<IReadOnlyList<TradeDto>> GetByStrategyIdAsync(string strategyId, DateTimeOffset? from = null, DateTimeOffset? to = null, int? limit = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TradeDto>>([]);
        public Task<IReadOnlyList<TradeDto>> GetByMarketIdAsync(string marketId, DateTimeOffset? from = null, DateTimeOffset? to = null, int? limit = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TradeDto>>([]);
        public Task<PagedResultDto<TradeDto>> GetPagedAsync(int page, int pageSize, string? strategyId = null, string? marketId = null, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new PagedResultDto<TradeDto>([], 0, page, pageSize));
        public Task AddAsync(TradeDto trade, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddRangeAsync(IEnumerable<TradeDto> trades, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> DeleteBeforeAsync(DateTimeOffset beforeUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
        public Task<PnLSummary> GetPnLSummaryAsync(string strategyId, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new PnLSummary(strategyId, 0m, 0m, 0m, 0, from, to));
    }

    private sealed class StubPositionRepository(IReadOnlyList<PositionDto> positions) : IPositionRepository
    {
        public Task<PositionDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(positions.FirstOrDefault(position => position.Id == id));
        public Task<PositionDto?> GetByMarketAndOutcomeAsync(Guid tradingAccountId, string marketId, OutcomeSide outcome, CancellationToken cancellationToken = default)
            => Task.FromResult<PositionDto?>(null);
        public Task<IReadOnlyList<PositionDto>> GetByTradingAccountIdAsync(Guid tradingAccountId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PositionDto>>([]);
        public Task<IReadOnlyList<PositionDto>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(positions);
        public Task<IReadOnlyList<PositionDto>> GetNonZeroAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(positions);
        public Task<PositionDto> GetOrCreateAsync(Guid tradingAccountId, string marketId, OutcomeSide outcome, CancellationToken cancellationToken = default)
            => Task.FromResult(positions.First());
        public Task AddAsync(PositionDto position, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(PositionDto position, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubRiskEventRepository(IReadOnlyList<RiskEventRecord> riskEvents) : IRiskEventRepository
    {
        public Task AddAsync(string code, RiskSeverity severity, string message, string? strategyId = null, string? contextJson = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<RiskEventRecord>> QueryAsync(string? strategyId = null, DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, int limit = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RiskEventRecord>>(riskEvents
                .Where(item => (!fromUtc.HasValue || item.CreatedAtUtc >= fromUtc)
                    && (!toUtc.HasValue || item.CreatedAtUtc <= toUtc)
                    && (strategyId is null || item.StrategyId == strategyId))
                .Take(limit)
                .ToArray());

        public Task<RiskEventRecord?> GetAsync(Guid riskEventId, CancellationToken cancellationToken = default)
            => Task.FromResult(riskEvents.FirstOrDefault(item => item.Id == riskEventId));
    }
}
