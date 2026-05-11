using Autotrade.Application.DTOs;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Application.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Moq;

namespace Autotrade.Trading.Tests.Risk;

public sealed class RiskDrilldownServiceTests
{
    [Fact]
    public async Task GetRiskEventAsync_ReturnsTriggerActionExposureOrdersAndKillSwitchLink()
    {
        var riskEventId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var orderId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var orderEventId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var startedAt = DateTimeOffset.Parse("2026-05-03T12:00:00Z");
        var endedAt = startedAt.AddSeconds(120);
        var contextJson = $$"""
            {
              "limitName": "Unhedged exposure timeout",
              "currentValue": 120,
              "threshold": 60,
              "unit": "seconds",
              "state": "breached",
              "selectedAction": "ForceHedge",
              "mitigationResult": "Hedge order submitted",
              "clientOrderIds": ["client-1"],
              "strategyId": "strategy-main",
              "marketId": "market-1",
              "tokenId": "token-yes",
              "hedgeTokenId": "token-no",
              "outcome": "Yes",
              "side": "Buy",
              "quantity": 10,
              "price": 0.45,
              "notional": 4.5,
              "startedAtUtc": "{{startedAt:O}}",
              "expiredAtUtc": "{{endedAt:O}}",
              "exposureDurationSeconds": 120,
              "configuredTimeoutSeconds": 60
            }
            """;
        var riskEvent = new RiskEventRecord(
            riskEventId,
            "RISK_UNHEDGED_TIMEOUT_FORCE_HEDGE",
            RiskSeverity.Critical,
            "Unhedged exposure timeout, force hedge executed.",
            "strategy-main",
            contextJson,
            endedAt,
            "market-1");
        var order = CreateOrder(orderId, "client-1");
        var orderEvent = new OrderEventDto(
            orderEventId,
            orderId,
            "client-1",
            "strategy-main",
            "market-1",
            OrderEventType.Cancelled,
            OrderStatus.Cancelled,
            "cancelled by unhedged exposure cleanup",
            null,
            "corr-1",
            endedAt,
            null);
        var service = CreateService(
            riskEvents: [riskEvent],
            orders: [order],
            orderEvents: [orderEvent],
            currentExposures: []);

        var drilldown = await service.GetRiskEventAsync(riskEventId);

        Assert.NotNull(drilldown);
        Assert.Equal(riskEventId, drilldown.Event.Id);
        Assert.Equal("Unhedged exposure timeout", drilldown.Trigger.LimitName);
        Assert.Equal(120m, drilldown.Trigger.CurrentValue);
        Assert.Equal(60m, drilldown.Trigger.Threshold);
        Assert.Equal("seconds", drilldown.Trigger.Unit);
        Assert.Equal("ForceHedge", drilldown.Action.SelectedAction);
        Assert.Equal("Hedge order submitted", drilldown.Action.MitigationResult);
        Assert.Contains(drilldown.AffectedOrders, item => item.ClientOrderId == "client-1" && item.Source == "orders");
        Assert.Contains(drilldown.AffectedOrders, item => item.DetailReference == $"order-events/{orderEventId}");
        Assert.NotNull(drilldown.Exposure);
        Assert.Equal(4.5m, drilldown.Exposure!.Notional);
        Assert.Equal(120d, drilldown.Exposure.DurationSeconds);
        Assert.Equal("HedgeAttempted", drilldown.Exposure.HedgeState);
        Assert.NotNull(drilldown.KillSwitch);
        Assert.Equal("Strategy", drilldown.KillSwitch!.Scope);
        Assert.Equal("HardStop", drilldown.KillSwitch.Level);

        var csv = RiskDrilldownCsvFormatter.FormatRiskEvents([drilldown]);
        Assert.Contains("RISK_UNHEDGED_TIMEOUT_FORCE_HEDGE", csv);
        Assert.Contains("ForceHedge", csv);
        Assert.Contains("client-1", csv);
    }

    [Fact]
    public async Task QueryUnhedgedExposuresAsync_ReturnsCurrentAndEventDerivedExposure()
    {
        var riskEventId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var startedAt = DateTimeOffset.Parse("2026-05-03T12:00:00Z");
        var eventExposure = new RiskEventRecord(
            riskEventId,
            "RISK_UNHEDGED_TIMEOUT_LOG",
            RiskSeverity.Warning,
            "Unhedged exposure timeout.",
            "strategy-main",
            $$"""
              {
                "marketId": "market-1",
                "tokenId": "token-yes",
                "hedgeTokenId": "token-no",
                "outcome": "Yes",
                "side": "Buy",
                "quantity": 3,
                "price": 0.40,
                "notional": 1.2,
                "startedAtUtc": "{{startedAt:O}}",
                "expiredAtUtc": "{{startedAt.AddSeconds(90):O}}",
                "exposureDurationSeconds": 90,
                "exitAction": "LogOnly"
              }
              """,
            startedAt.AddSeconds(90),
            "market-1");
        var currentExposure = new UnhedgedExposureSnapshot(
            "strategy-main",
            "market-1",
            "token-open",
            "token-open-hedge",
            OutcomeSide.No,
            OrderSide.Buy,
            5m,
            0.2m,
            1m,
            startedAt.AddMinutes(10));
        var service = CreateService(
            riskEvents: [eventExposure],
            orders: [],
            orderEvents: [],
            currentExposures: [currentExposure]);

        var response = await service.QueryUnhedgedExposuresAsync(new RiskDrilldownQuery(
            StrategyId: "strategy-main",
            MarketId: "market-1",
            FromUtc: startedAt.AddMinutes(-1),
            ToUtc: startedAt.AddHours(1),
            Limit: 10));

        Assert.Equal(2, response.Count);
        Assert.Contains(response.Exposures, item => item.Source == "risk_state" && item.TokenId == "token-open");
        Assert.Contains(response.Exposures, item => item.Source == "risk_event" && item.EvidenceId == riskEventId);

        var csv = RiskDrilldownCsvFormatter.FormatUnhedgedExposures(response.Exposures);
        Assert.Contains("unhedged_exposures", csv);
        Assert.Contains("token-open", csv);
        Assert.Contains(riskEventId.ToString(), csv);
    }

    private static RiskDrilldownService CreateService(
        IReadOnlyList<RiskEventRecord> riskEvents,
        IReadOnlyList<OrderDto> orders,
        IReadOnlyList<OrderEventDto> orderEvents,
        IReadOnlyList<UnhedgedExposureSnapshot> currentExposures)
    {
        var riskRepository = new Mock<IRiskEventRepository>();
        riskRepository
            .Setup(repository => repository.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => riskEvents.FirstOrDefault(item => item.Id == id));
        riskRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<string?>(),
                It.IsAny<DateTimeOffset?>(),
                It.IsAny<DateTimeOffset?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? strategyId, DateTimeOffset? from, DateTimeOffset? to, int limit, CancellationToken _) =>
                riskEvents
                    .Where(item => (strategyId is null || item.StrategyId == strategyId)
                        && (!from.HasValue || item.CreatedAtUtc >= from.Value)
                        && (!to.HasValue || item.CreatedAtUtc <= to.Value))
                    .Take(limit)
                    .ToArray());

        var orderRepository = new Mock<IOrderRepository>();
        orderRepository
            .Setup(repository => repository.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => orders.FirstOrDefault(item => item.Id == id));
        orderRepository
            .Setup(repository => repository.GetByClientOrderIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string clientOrderId, CancellationToken _) => orders.FirstOrDefault(item => item.ClientOrderId == clientOrderId));

        var orderEventRepository = new Mock<IOrderEventRepository>();
        orderEventRepository
            .Setup(repository => repository.GetByOrderIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid orderId, CancellationToken _) => orderEvents.Where(item => item.OrderId == orderId).ToArray());
        orderEventRepository
            .Setup(repository => repository.GetByClientOrderIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string clientOrderId, CancellationToken _) => orderEvents.Where(item => item.ClientOrderId == clientOrderId).ToArray());
        orderEventRepository
            .Setup(repository => repository.GetPagedAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<OrderEventType?>(),
                It.IsAny<DateTimeOffset?>(),
                It.IsAny<DateTimeOffset?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((int page, int pageSize, string? strategyId, string? marketId, OrderEventType? eventType, DateTimeOffset? from, DateTimeOffset? to, CancellationToken _) =>
            {
                var items = orderEvents
                    .Where(item => (strategyId is null || item.StrategyId == strategyId)
                        && (marketId is null || item.MarketId == marketId)
                        && (!eventType.HasValue || item.EventType == eventType.Value)
                        && (!from.HasValue || item.CreatedAtUtc >= from.Value)
                        && (!to.HasValue || item.CreatedAtUtc <= to.Value))
                    .Take(pageSize)
                    .ToArray();
                return new PagedResultDto<OrderEventDto>(items, items.Length, page, pageSize);
            });

        var riskManager = new Mock<IRiskManager>();
        riskManager
            .Setup(manager => manager.GetStateSnapshot())
            .Returns(new RiskStateSnapshot(
                0m,
                0,
                100m,
                100m,
                0m,
                new Dictionary<string, decimal>(),
                new Dictionary<string, decimal>(),
                new Dictionary<string, int>(),
                currentExposures));
        riskManager
            .Setup(manager => manager.GetStrategyKillSwitchState(It.IsAny<string>()))
            .Returns(new KillSwitchState
            {
                IsActive = true,
                Level = KillSwitchLevel.HardStop,
                ReasonCode = "RISK_UNHEDGED_TIMEOUT_FORCE_HEDGE",
                Reason = "Unhedged exposure timeout",
                ActivatedAtUtc = DateTimeOffset.Parse("2026-05-03T12:02:00Z")
            });
        riskManager
            .Setup(manager => manager.GetKillSwitchState())
            .Returns(KillSwitchState.Inactive);

        return new RiskDrilldownService(
            riskRepository.Object,
            orderEventRepository.Object,
            orderRepository.Object,
            riskManager.Object);
    }

    private static OrderDto CreateOrder(Guid orderId, string clientOrderId)
        => new(
            orderId,
            Guid.NewGuid(),
            "market-1",
            "token-yes",
            "strategy-main",
            clientOrderId,
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
            0m,
            OrderStatus.Cancelled,
            null,
            DateTimeOffset.Parse("2026-05-03T12:00:00Z"),
            DateTimeOffset.Parse("2026-05-03T12:02:00Z"));
}
