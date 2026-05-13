using System.Text.Json;
using Autotrade.Application.DTOs;
using Autotrade.Application.Readiness;
using Autotrade.Application.RunSessions;
using Autotrade.MarketData.Application.Contract.Tape;
using Autotrade.Strategy.Application.Audit;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Decisions;
using Autotrade.Strategy.Application.RunSessions;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Strategy.Tests.Audit;

public sealed class ReplayExportServiceTests
{
    [Fact]
    public async Task ExportAsync_ReturnsReplayPackageWithAllEvidenceAndRedactions()
    {
        var startedAt = DateTimeOffset.Parse("2026-05-03T12:00:00Z");
        var sessionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var orderId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var tradeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var positionId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var riskEventId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var tradingAccountId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var session = new PaperRunSessionRecord(
            sessionId,
            "Paper",
            "cfg-v1",
            ["dual_leg_arbitrage"],
            "{\"maxExposure\":100,\"privateKey\":\"private-key-value\"}",
            "test",
            startedAt,
            startedAt.AddHours(1),
            "done",
            false,
            false);
        var decision = new StrategyDecisionRecord(
            Guid.NewGuid(),
            "dual_leg_arbitrage",
            "Buy",
            "edge",
            "market-1",
            $$"""
              {
                "apiKey": "api-key-value",
                "orderId": "{{orderId}}",
                "riskEventId": "{{riskEventId}}"
              }
              """,
            startedAt.AddMinutes(1),
            "cfg-v1",
            "corr-1",
            "Paper",
            sessionId);
        var orderEvent = new OrderEventDto(
            Guid.NewGuid(),
            orderId,
            "client-1",
            "dual_leg_arbitrage",
            "market-1",
            OrderEventType.Accepted,
            OrderStatus.Open,
            "accepted",
            $$"""
              {
                "runSessionId": "{{sessionId}}",
                "riskEventId": "{{riskEventId}}",
                "secret": "super-secret"
              }
              """,
            "corr-1",
            startedAt.AddMinutes(2),
            sessionId);
        var order = new OrderDto(
            orderId,
            tradingAccountId,
            "market-1",
            "token-yes",
            "dual_leg_arbitrage",
            "client-1",
            "exchange-1",
            "corr-1",
            OutcomeSide.Yes,
            OrderSide.Buy,
            OrderType.Limit,
            TimeInForce.Gtc,
            null,
            false,
            0.45m,
            10m,
            10m,
            OrderStatus.Filled,
            null,
            startedAt.AddMinutes(2),
            startedAt.AddMinutes(3),
            "salt-value",
            "timestamp-value");
        var trade = new TradeDto(
            tradeId,
            orderId,
            tradingAccountId,
            "client-1",
            "dual_leg_arbitrage",
            "market-1",
            "token-yes",
            OutcomeSide.Yes,
            OrderSide.Buy,
            0.46m,
            10m,
            "exchange-trade-1",
            0.01m,
            "corr-1",
            startedAt.AddMinutes(3));
        var position = new PositionDto(
            positionId,
            tradingAccountId,
            "market-1",
            OutcomeSide.Yes,
            10m,
            0.45m,
            1.2m,
            startedAt.AddMinutes(4));
        var riskEvent = new RiskEventRecord(
            riskEventId,
            "RISK_MAX_EXPOSURE",
            RiskSeverity.Warning,
            "limit reached",
            "dual_leg_arbitrage",
            $$"""
              {
                "marketId": "market-1",
                "runSessionId": "{{sessionId}}",
                "correlationId": "corr-1",
                "authorization": "authorization-token"
              }
              """,
            startedAt.AddMinutes(5),
            "market-1");
        var readiness = CreateReadinessReport(startedAt);
        var timeline = new AuditTimeline(
            startedAt.AddMinutes(6),
            1,
            100,
            new AuditTimelineQuery(RunSessionId: sessionId),
            [
                new AuditTimelineItem(
                    Guid.NewGuid(),
                    startedAt.AddMinutes(1),
                    AuditTimelineItemType.StrategyDecision,
                    "strategy",
                    "dual_leg_arbitrage",
                    "decision",
                    $"strategy-decisions/{decision.DecisionId}",
                    "dual_leg_arbitrage",
                    "market-1",
                    orderId,
                    "client-1",
                    sessionId,
                    riskEventId,
                    "corr-1",
                    "{\"password\":\"timeline-password\"}")
            ]);
        var auditTimelineService = new StubAuditTimelineService(timeline);
        var service = CreateService(
            auditTimelineService,
            session,
            readiness,
            [decision],
            [orderEvent],
            [order],
            [trade],
            [position],
            [riskEvent]);

        var package = await service.ExportAsync(new ReplayExportQuery(
            StrategyId: "dual_leg_arbitrage",
            MarketId: "market-1",
            RunSessionId: sessionId,
            CorrelationId: "corr-1",
            Limit: 100));

        Assert.Equal(ReplayExportService.ContractVersion, package.ContractVersion);
        Assert.NotNull(package.RunSession);
        Assert.NotNull(package.Readiness);
        Assert.Equal(1, package.Timeline.Count);
        Assert.Single(package.Evidence.Decisions);
        Assert.Single(package.Evidence.OrderEvents);
        Assert.Single(package.Evidence.Orders);
        Assert.Single(package.Evidence.Trades);
        Assert.Single(package.Evidence.Positions);
        Assert.Single(package.Evidence.RiskEvents);
        Assert.Contains(package.StrategyConfigVersions, version =>
            version.StrategyId == "dual_leg_arbitrage" && version.ConfigVersion == "cfg-v1");
        Assert.Equal("market-1", package.Evidence.Orders[0].MarketId);
        Assert.Equal(0.46m, package.Evidence.Trades[0].Price);
        Assert.Equal(4.5m, package.Evidence.Positions[0].Notional);
        Assert.Contains("TradingAccountId", package.Redaction.ExcludedFields);
        Assert.Equal(sessionId, auditTimelineService.LastQuery?.RunSessionId);

        var evidenceJson = JsonSerializer.Serialize(package.Evidence);
        Assert.DoesNotContain("TradingAccountId", evidenceJson, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderSalt", evidenceJson, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderTimestamp", evidenceJson, StringComparison.Ordinal);

        var json = JsonSerializer.Serialize(package);
        Assert.DoesNotContain(tradingAccountId.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-key-value", json, StringComparison.Ordinal);
        Assert.DoesNotContain("api-key-value", json, StringComparison.Ordinal);
        Assert.DoesNotContain("readiness-api-key", json, StringComparison.Ordinal);
        Assert.DoesNotContain("readiness-token", json, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("authorization-token", json, StringComparison.Ordinal);
        Assert.DoesNotContain("timeline-password", json, StringComparison.Ordinal);
        Assert.Contains("[redacted]", json, StringComparison.Ordinal);
        Assert.Equal("docs/operations/replay-export-schema.md", package.ExportReferences.Schema);
    }

    [Fact]
    public async Task ExportAsync_WhenReadinessProbeFailsReturnsEvidenceWithCompletenessNote()
    {
        var startedAt = DateTimeOffset.Parse("2026-05-03T12:00:00Z");
        var sessionId = Guid.NewGuid();
        var session = new PaperRunSessionRecord(
            sessionId,
            "Paper",
            "cfg-v1",
            ["dual_leg_arbitrage"],
            "{}",
            "test",
            startedAt,
            startedAt.AddMinutes(30),
            "done",
            false,
            false);
        var service = new ReplayExportService(
            new StubAuditTimelineService(CreateEmptyTimeline()),
            new StubDecisionQueryService([]),
            new StubOrderEventRepository([]),
            new StubOrderRepository([]),
            new StubTradeRepository([]),
            new StubPositionRepository([]),
            new StubRiskEventRepository([]),
            new StubPaperRunSessionService(session),
            new ThrowingReadinessReportService());

        var package = await service.ExportAsync(new ReplayExportQuery(RunSessionId: sessionId));

        Assert.Null(package.Readiness);
        Assert.Contains(package.CompletenessNotes, note =>
            note.Contains("Readiness state was unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExportAsync_IncludesMarketTapeSliceWhenMarketQueryIsScoped()
    {
        var startedAt = DateTimeOffset.Parse("2026-05-03T12:00:00Z");
        var sessionId = Guid.NewGuid();
        var stoppedAt = startedAt.AddMinutes(30);
        var session = new PaperRunSessionRecord(
            sessionId,
            "Paper",
            "cfg-v1",
            ["llm_opportunity"],
            "{}",
            "test",
            startedAt,
            stoppedAt,
            "done",
            false,
            false);
        var replayReader = new StubMarketReplayReader(new MarketTapeReplaySlice(
            new MarketTapeQuery("market-1"),
            Array.Empty<MarketPriceTickDto>(),
            [
                new OrderBookTopTickDto(
                    Guid.Empty,
                    "market-1",
                    "token-yes",
                    startedAt.AddMinutes(1),
                    0.49m,
                    10m,
                    0.51m,
                    10m,
                    0.02m,
                    "test",
                    "seq-1",
                    "{}",
                    startedAt.AddMinutes(1))
            ],
            Array.Empty<OrderBookDepthSnapshotDto>(),
            Array.Empty<ClobTradeTickDto>(),
            Array.Empty<MarketResolutionEventDto>(),
            ["test tape note"]));
        var service = CreateService(
            new StubAuditTimelineService(CreateEmptyTimeline()),
            session,
            CreateReadinessReport(startedAt),
            [],
            [],
            [],
            [],
            [],
            [],
            replayReader);

        var package = await service.ExportAsync(new ReplayExportQuery(
            MarketId: "market-1",
            RunSessionId: sessionId,
            Limit: 100));

        Assert.NotNull(package.MarketTape);
        Assert.Single(package.MarketTape.TopTicks);
        Assert.Equal("token-yes", package.MarketTape.TopTicks[0].TokenId);
        Assert.Equal("test tape note", package.MarketTape.CompletenessNotes[0]);
        Assert.Equal(startedAt, replayReader.LastQuery?.FromUtc);
        Assert.Equal(stoppedAt, replayReader.LastQuery?.ToUtc);
        Assert.Equal(stoppedAt, replayReader.LastQuery?.AsOfUtc);
        Assert.DoesNotContain(package.CompletenessNotes, note =>
            note.Contains("Market tape was not included", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExportAsync_ScopedQueryWithoutMatchedEvidenceDoesNotIncludeUnrelatedPositions()
    {
        var startedAt = DateTimeOffset.Parse("2026-05-03T12:00:00Z");
        var tradingAccountId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var session = new PaperRunSessionRecord(
            Guid.NewGuid(),
            "Paper",
            "cfg-v1",
            ["dual_leg_arbitrage"],
            "{}",
            "test",
            startedAt,
            startedAt.AddMinutes(30),
            "done",
            false,
            false);
        var unrelatedPosition = new PositionDto(
            Guid.NewGuid(),
            tradingAccountId,
            "unrelated-market",
            OutcomeSide.Yes,
            10m,
            0.45m,
            0m,
            startedAt.AddMinutes(1));
        var service = CreateService(
            new StubAuditTimelineService(CreateEmptyTimeline()),
            session,
            CreateReadinessReport(startedAt),
            [],
            [],
            [],
            [],
            [unrelatedPosition],
            []);

        var package = await service.ExportAsync(new ReplayExportQuery(
            StrategyId: "missing-strategy",
            Limit: 100));

        Assert.Empty(package.Evidence.Positions);
        Assert.Contains(package.CompletenessNotes, note =>
            note.Contains("No non-zero positions matched the replay query markets.", StringComparison.Ordinal));
    }

    private static ReplayExportService CreateService(
        IAuditTimelineService auditTimelineService,
        PaperRunSessionRecord session,
        ReadinessReport readiness,
        IReadOnlyList<StrategyDecisionRecord> decisions,
        IReadOnlyList<OrderEventDto> orderEvents,
        IReadOnlyList<OrderDto> orders,
        IReadOnlyList<TradeDto> trades,
        IReadOnlyList<PositionDto> positions,
        IReadOnlyList<RiskEventRecord> riskEvents,
        IMarketReplayReader? marketReplayReader = null)
        => new(
            auditTimelineService,
            new StubDecisionQueryService(decisions),
            new StubOrderEventRepository(orderEvents),
            new StubOrderRepository(orders),
            new StubTradeRepository(trades),
            new StubPositionRepository(positions),
            new StubRiskEventRepository(riskEvents),
            new StubPaperRunSessionService(session),
            new StubReadinessReportService(readiness),
            marketReplayReader);

    private static ReadinessReport CreateReadinessReport(DateTimeOffset checkedAtUtc)
        => new(
            "readiness.v1",
            checkedAtUtc,
            ReadinessOverallStatus.Ready,
            [
                new ReadinessCheckResult(
                    "credentials.polymarket",
                    ReadinessCheckCategory.Credentials,
                    ReadinessCheckRequirement.LiveOnly,
                    ReadinessCheckStatus.Ready,
                    "test",
                    checkedAtUtc,
                    "credentials present",
                    "none",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["apiKey"] = "readiness-api-key",
                        ["diagnostic"] = "authorization=readiness-token",
                        ["public"] = "safe"
                    })
            ],
            [
                new ReadinessCapabilityResult(
                    ReadinessCapability.PaperTrading,
                    ReadinessOverallStatus.Ready,
                    [],
                    "ready")
            ]);

    private static AuditTimeline CreateEmptyTimeline()
        => new(
            DateTimeOffset.Parse("2026-05-03T12:00:00Z"),
            0,
            100,
            new AuditTimelineQuery(),
            []);

    private sealed class StubAuditTimelineService(AuditTimeline timeline) : IAuditTimelineService
    {
        public AuditTimelineQuery? LastQuery { get; private set; }

        public Task<AuditTimeline> QueryAsync(
            AuditTimelineQuery query,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult(timeline with { Query = query });
        }
    }

    private sealed class StubDecisionQueryService(IReadOnlyList<StrategyDecisionRecord> decisions)
        : IStrategyDecisionQueryService
    {
        public Task<IReadOnlyList<StrategyDecision>> QueryAsync(
            StrategyDecisionQuery query,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<StrategyDecision>>([]);

        public Task<IReadOnlyList<StrategyDecisionRecord>> QueryRecordsAsync(
            StrategyDecisionQuery query,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<StrategyDecisionRecord>>(decisions
                .Where(item => Matches(query.StrategyId, item.StrategyId)
                    && Matches(query.MarketId, item.MarketId)
                    && Matches(query.CorrelationId, item.CorrelationId)
                    && (!query.RunSessionId.HasValue || item.RunSessionId == query.RunSessionId.Value)
                    && (!query.FromUtc.HasValue || item.TimestampUtc >= query.FromUtc.Value)
                    && (!query.ToUtc.HasValue || item.TimestampUtc <= query.ToUtc.Value))
                .Take(query.Limit)
                .ToArray());

        public Task<StrategyDecisionRecord?> GetAsync(
            Guid decisionId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(decisions.FirstOrDefault(item => item.DecisionId == decisionId));
    }

    private sealed class StubOrderEventRepository(IReadOnlyList<OrderEventDto> events) : IOrderEventRepository
    {
        public Task<IReadOnlyList<OrderEventDto>> GetByOrderIdAsync(
            Guid orderId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OrderEventDto>>(events
                .Where(item => item.OrderId == orderId)
                .ToArray());

        public Task<IReadOnlyList<OrderEventDto>> GetByClientOrderIdAsync(
            string clientOrderId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OrderEventDto>>(events
                .Where(item => item.ClientOrderId == clientOrderId)
                .ToArray());

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
            => Task.FromResult<IReadOnlyList<OrderEventDto>>(events
                .Where(item => Matches(strategyId, item.StrategyId))
                .Take(limit ?? int.MaxValue)
                .ToArray());

        public Task<PagedResultDto<OrderEventDto>> GetPagedAsync(
            int page,
            int pageSize,
            string? strategyId = null,
            string? marketId = null,
            OrderEventType? eventType = null,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            CancellationToken cancellationToken = default)
        {
            var items = events
                .Where(item => Matches(strategyId, item.StrategyId)
                    && Matches(marketId, item.MarketId)
                    && (!eventType.HasValue || item.EventType == eventType.Value)
                    && (!from.HasValue || item.CreatedAtUtc >= from.Value)
                    && (!to.HasValue || item.CreatedAtUtc <= to.Value))
                .Take(pageSize)
                .ToArray();

            return Task.FromResult(new PagedResultDto<OrderEventDto>(items, items.Length, page, pageSize));
        }

        public Task AddAsync(OrderEventDto orderEvent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task AddRangeAsync(IEnumerable<OrderEventDto> orderEvents, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<int> DeleteBeforeAsync(DateTimeOffset beforeUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private sealed class StubOrderRepository(IReadOnlyList<OrderDto> orders) : IOrderRepository
    {
        public Task AddAsync(OrderDto order, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task AddRangeAsync(IEnumerable<OrderDto> orders, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UpdateAsync(OrderDto order, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<OrderDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(orders.FirstOrDefault(order => order.Id == id));

        public Task<OrderDto?> GetByClientOrderIdAsync(string clientOrderId, CancellationToken cancellationToken = default)
            => Task.FromResult(orders.FirstOrDefault(order => order.ClientOrderId == clientOrderId));

        public Task<OrderDto?> GetByExchangeOrderIdAsync(string exchangeOrderId, CancellationToken cancellationToken = default)
            => Task.FromResult(orders.FirstOrDefault(order => order.ExchangeOrderId == exchangeOrderId));

        public Task<IReadOnlyList<OrderDto>> GetOpenOrdersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OrderDto>>(orders
                .Where(order => order.Status is OrderStatus.Open or OrderStatus.PartiallyFilled)
                .ToArray());

        public Task<IReadOnlyList<OrderDto>> GetByStrategyIdAsync(
            string strategyId,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            int? limit = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OrderDto>>(orders
                .Where(order => Matches(strategyId, order.StrategyId))
                .Take(limit ?? int.MaxValue)
                .ToArray());

        public Task<IReadOnlyList<OrderDto>> GetByMarketIdAsync(
            string marketId,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            int? limit = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OrderDto>>(orders
                .Where(order => Matches(marketId, order.MarketId))
                .Take(limit ?? int.MaxValue)
                .ToArray());

        public Task<IReadOnlyList<OrderDto>> GetByStatusAsync(
            OrderStatus status,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            int? limit = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OrderDto>>(orders
                .Where(order => order.Status == status)
                .Take(limit ?? int.MaxValue)
                .ToArray());

        public Task<PagedResultDto<OrderDto>> GetPagedAsync(
            int page,
            int pageSize,
            string? strategyId = null,
            string? marketId = null,
            OrderStatus? status = null,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            CancellationToken cancellationToken = default)
        {
            var items = orders
                .Where(order => Matches(strategyId, order.StrategyId)
                    && Matches(marketId, order.MarketId)
                    && (!status.HasValue || order.Status == status.Value)
                    && (!from.HasValue || order.CreatedAtUtc >= from.Value)
                    && (!to.HasValue || order.CreatedAtUtc <= to.Value))
                .Take(pageSize)
                .ToArray();

            return Task.FromResult(new PagedResultDto<OrderDto>(items, items.Length, page, pageSize));
        }

        public Task<int> DeleteBeforeAsync(DateTimeOffset beforeUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private sealed class StubTradeRepository(IReadOnlyList<TradeDto> trades) : ITradeRepository
    {
        public Task<TradeDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(trades.FirstOrDefault(trade => trade.Id == id));

        public Task<IReadOnlyList<TradeDto>> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TradeDto>>(trades
                .Where(trade => trade.OrderId == orderId)
                .ToArray());

        public Task<IReadOnlyList<TradeDto>> GetByClientOrderIdAsync(
            string clientOrderId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TradeDto>>(trades
                .Where(trade => trade.ClientOrderId == clientOrderId)
                .ToArray());

        public Task<TradeDto?> GetByExchangeTradeIdAsync(
            string exchangeTradeId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(trades.FirstOrDefault(trade => trade.ExchangeTradeId == exchangeTradeId));

        public Task<IReadOnlyList<TradeDto>> GetByStrategyIdAsync(
            string strategyId,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            int? limit = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TradeDto>>(trades
                .Where(trade => Matches(strategyId, trade.StrategyId))
                .Take(limit ?? int.MaxValue)
                .ToArray());

        public Task<IReadOnlyList<TradeDto>> GetByMarketIdAsync(
            string marketId,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            int? limit = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TradeDto>>(trades
                .Where(trade => Matches(marketId, trade.MarketId))
                .Take(limit ?? int.MaxValue)
                .ToArray());

        public Task<PagedResultDto<TradeDto>> GetPagedAsync(
            int page,
            int pageSize,
            string? strategyId = null,
            string? marketId = null,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            CancellationToken cancellationToken = default)
        {
            var items = trades
                .Where(trade => Matches(strategyId, trade.StrategyId)
                    && Matches(marketId, trade.MarketId)
                    && (!from.HasValue || trade.CreatedAtUtc >= from.Value)
                    && (!to.HasValue || trade.CreatedAtUtc <= to.Value))
                .Take(pageSize)
                .ToArray();

            return Task.FromResult(new PagedResultDto<TradeDto>(items, items.Length, page, pageSize));
        }

        public Task AddAsync(TradeDto trade, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task AddRangeAsync(IEnumerable<TradeDto> trades, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<int> DeleteBeforeAsync(DateTimeOffset beforeUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<PnLSummary> GetPnLSummaryAsync(
            string strategyId,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new PnLSummary(strategyId, 0m, 0m, 0m, 0, from, to));
    }

    private sealed class StubPositionRepository(IReadOnlyList<PositionDto> positions) : IPositionRepository
    {
        public Task<PositionDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(positions.FirstOrDefault(position => position.Id == id));

        public Task<PositionDto?> GetByMarketAndOutcomeAsync(
            Guid tradingAccountId,
            string marketId,
            OutcomeSide outcome,
            CancellationToken cancellationToken = default)
            => Task.FromResult(positions.FirstOrDefault(position =>
                position.TradingAccountId == tradingAccountId
                && position.MarketId == marketId
                && position.Outcome == outcome));

        public Task<IReadOnlyList<PositionDto>> GetByTradingAccountIdAsync(
            Guid tradingAccountId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PositionDto>>(positions
                .Where(position => position.TradingAccountId == tradingAccountId)
                .ToArray());

        public Task<IReadOnlyList<PositionDto>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(positions);

        public Task<IReadOnlyList<PositionDto>> GetNonZeroAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PositionDto>>(positions
                .Where(position => position.Quantity != 0m)
                .ToArray());

        public Task<PositionDto> GetOrCreateAsync(
            Guid tradingAccountId,
            string marketId,
            OutcomeSide outcome,
            CancellationToken cancellationToken = default)
            => Task.FromResult(positions.First());

        public Task AddAsync(PositionDto position, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UpdateAsync(PositionDto position, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubRiskEventRepository(IReadOnlyList<RiskEventRecord> riskEvents) : IRiskEventRepository
    {
        public Task AddAsync(
            string code,
            RiskSeverity severity,
            string message,
            string? strategyId = null,
            string? contextJson = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<RiskEventRecord>> QueryAsync(
            string? strategyId = null,
            DateTimeOffset? fromUtc = null,
            DateTimeOffset? toUtc = null,
            int limit = 100,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RiskEventRecord>>(riskEvents
                .Where(item => Matches(strategyId, item.StrategyId)
                    && (!fromUtc.HasValue || item.CreatedAtUtc >= fromUtc.Value)
                    && (!toUtc.HasValue || item.CreatedAtUtc <= toUtc.Value))
                .Take(limit)
                .ToArray());

        public Task<RiskEventRecord?> GetAsync(Guid riskEventId, CancellationToken cancellationToken = default)
            => Task.FromResult(riskEvents.FirstOrDefault(item => item.Id == riskEventId));
    }

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

    private sealed class StubReadinessReportService(ReadinessReport report) : IReadinessReportService
    {
        public Task<ReadinessReport> GetReportAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(report);
    }

    private sealed class ThrowingReadinessReportService : IReadinessReportService
    {
        public Task<ReadinessReport> GetReportAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("readiness offline");
    }

    private sealed class StubMarketReplayReader(MarketTapeReplaySlice slice) : IMarketReplayReader
    {
        public MarketTapeQuery? LastQuery { get; private set; }

        public Task<MarketTapeReplaySlice> GetReplaySliceAsync(
            MarketTapeQuery query,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult(slice with { Query = query });
        }
    }

    private static bool Matches(string? expected, string? actual)
        => string.IsNullOrWhiteSpace(expected)
            || string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
}
