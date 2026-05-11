using Autotrade.Application.DTOs;
using Autotrade.Strategy.Application.Audit;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Decisions;
using Autotrade.Strategy.Domain.Entities;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Strategy.Tests.Audit;

public sealed class AuditTimelineServiceTests
{
    [Fact]
    public async Task QueryAsync_ReturnsEmptyTimelineWhenNoEvidenceExists()
    {
        var service = CreateService([], [], [], []);

        var timeline = await service.QueryAsync(new AuditTimelineQuery(Limit: 25));

        Assert.Equal(0, timeline.Count);
        Assert.Equal(25, timeline.Limit);
        Assert.Empty(timeline.Items);
    }

    [Fact]
    public async Task QueryAsync_MergesPartialEvidenceWithRunStrategyMarketAndCorrelationFilters()
    {
        var runSessionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var orderId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var riskEventId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var fromUtc = DateTimeOffset.Parse("2026-05-03T12:00:00Z");
        var decision = new StrategyDecisionRecord(
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            "strategy-main",
            "Buy",
            "edge",
            "market-1",
            $$"""
              {
                "correlationId": "corr-1",
                "orderReferences": [
                  {
                    "orderId": "{{orderId}}",
                    "clientOrderId": "client-1"
                  }
                ]
              }
              """,
            fromUtc.AddMinutes(1),
            "cfg-v1",
            "corr-1",
            "Paper",
            runSessionId);
        var orderEvent = new OrderEventDto(
            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            orderId,
            "client-1",
            "strategy-main",
            "market-1",
            OrderEventType.Accepted,
            OrderStatus.Open,
            "accepted",
            null,
            "corr-1",
            fromUtc.AddMinutes(2),
            runSessionId);
        var riskEvent = new RiskEventRecord(
            riskEventId,
            "RISK_MAX_EXPOSURE",
            RiskSeverity.Warning,
            "exposure limit reached",
            "strategy-main",
            $$"""
              {
                "marketId": "market-1",
                "runSessionId": "{{runSessionId}}",
                "correlationId": "corr-1"
              }
              """,
            fromUtc.AddMinutes(3),
            "market-1");
        var service = CreateService([decision], [orderEvent], [riskEvent], []);

        var timeline = await service.QueryAsync(new AuditTimelineQuery(
            "strategy-main",
            "market-1",
            RunSessionId: runSessionId,
            CorrelationId: "corr-1",
            FromUtc: fromUtc,
            ToUtc: fromUtc.AddHours(1),
            Limit: 50));

        Assert.Equal(3, timeline.Count);
        Assert.All(timeline.Items, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Actor));
            Assert.False(string.IsNullOrWhiteSpace(item.Source));
            Assert.False(string.IsNullOrWhiteSpace(item.DetailReference));
            Assert.Equal("corr-1", item.CorrelationId);
        });
        Assert.Contains(timeline.Items, item => item.Type == AuditTimelineItemType.StrategyDecision);
        Assert.Contains(timeline.Items, item => item.Type == AuditTimelineItemType.OrderEvent);
        Assert.Contains(timeline.Items, item => item.Type == AuditTimelineItemType.RiskEvent);
    }

    [Fact]
    public async Task QueryAsync_OrdersDenseTimelineDeterministically()
    {
        var timestamp = DateTimeOffset.Parse("2026-05-03T12:00:00Z");
        var decision = new StrategyDecisionRecord(
            Guid.Parse("40000000-0000-0000-0000-000000000000"),
            "strategy-main",
            "Hold",
            "no edge",
            "market-1",
            "{}",
            timestamp,
            "cfg-v1",
            "corr-1",
            "Paper");
        var orderEvent = new OrderEventDto(
            Guid.Parse("30000000-0000-0000-0000-000000000000"),
            Guid.Parse("20000000-0000-0000-0000-000000000000"),
            "client-1",
            "strategy-main",
            "market-1",
            OrderEventType.Rejected,
            OrderStatus.Rejected,
            "rejected",
            null,
            "corr-1",
            timestamp,
            null);
        var riskEvent = new RiskEventRecord(
            Guid.Parse("10000000-0000-0000-0000-000000000000"),
            "RISK_TEST",
            RiskSeverity.Warning,
            "risk",
            "strategy-main",
            "{\"marketId\":\"market-1\",\"correlationId\":\"corr-1\"}",
            timestamp,
            "market-1");
        var command = new CommandAuditLog(
            "status",
            "{\"strategyId\":\"strategy-main\",\"marketId\":\"market-1\",\"correlationId\":\"corr-1\"}",
            "alice",
            true,
            0,
            5,
            timestamp);
        var service = CreateService([decision], [orderEvent], [riskEvent], [command]);

        var timeline = await service.QueryAsync(new AuditTimelineQuery(
            StrategyId: "strategy-main",
            MarketId: "market-1",
            CorrelationId: "corr-1",
            FromUtc: timestamp.AddMinutes(-1),
            ToUtc: timestamp.AddMinutes(1),
            Limit: 10));

        Assert.Collection(
            timeline.Items,
            item => Assert.Equal(AuditTimelineItemType.StrategyDecision, item.Type),
            item => Assert.Equal(AuditTimelineItemType.OrderEvent, item.Type),
            item => Assert.Equal(AuditTimelineItemType.RiskEvent, item.Type),
            item => Assert.Equal(AuditTimelineItemType.CommandAudit, item.Type));
    }

    private static AuditTimelineService CreateService(
        IReadOnlyList<StrategyDecisionRecord> decisions,
        IReadOnlyList<OrderEventDto> orderEvents,
        IReadOnlyList<RiskEventRecord> riskEvents,
        IReadOnlyList<CommandAuditLog> commandAudits)
        => new(
            new StubDecisionQueryService(decisions),
            new StubOrderEventRepository(orderEvents),
            new StubRiskEventRepository(riskEvents),
            new StubCommandAuditRepository(commandAudits));

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
                .Where(item => item.StrategyId == strategyId)
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
                    && (!from.HasValue || item.CreatedAtUtc >= from.Value)
                    && (!to.HasValue || item.CreatedAtUtc <= to.Value)
                    && (!eventType.HasValue || item.EventType == eventType.Value))
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

    private sealed class StubRiskEventRepository(IReadOnlyList<RiskEventRecord> events) : IRiskEventRepository
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
            => Task.FromResult<IReadOnlyList<RiskEventRecord>>(events
                .Where(item => Matches(strategyId, item.StrategyId)
                    && (!fromUtc.HasValue || item.CreatedAtUtc >= fromUtc.Value)
                    && (!toUtc.HasValue || item.CreatedAtUtc <= toUtc.Value))
                .Take(limit)
                .ToArray());

        public Task<RiskEventRecord?> GetAsync(Guid riskEventId, CancellationToken cancellationToken = default)
            => Task.FromResult(events.FirstOrDefault(item => item.Id == riskEventId));
    }

    private sealed class StubCommandAuditRepository(IReadOnlyList<CommandAuditLog> commands)
        : ICommandAuditRepository
    {
        public Task AddAsync(CommandAuditLog log, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<CommandAuditLog>> QueryAsync(
            CommandAuditQuery query,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CommandAuditLog>>(commands
                .Where(item => (!query.FromUtc.HasValue || item.CreatedAtUtc >= query.FromUtc.Value)
                    && (!query.ToUtc.HasValue || item.CreatedAtUtc <= query.ToUtc.Value)
                    && Matches(query.CommandName, item.CommandName)
                    && Matches(query.Actor, item.Actor))
                .Take(query.Limit)
                .ToArray());
    }

    private static bool Matches(string? expected, string? actual)
        => string.IsNullOrWhiteSpace(expected)
            || string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
}
