using Autotrade.EventBus.CAP;
using Autotrade.Trading.Domain.Events;
using Autotrade.Trading.Domain.Events.Converters;
using Autotrade.Trading.Domain.Shared.Enums;
using Autotrade.Trading.Domain.Shared.IntegrationEvents;

namespace Autotrade.Trading.Tests;

public sealed class TradingIntegrationDtoConverterTests
{
    [Fact]
    public void Convert_ShouldMapOrderAcceptedEventToIntegrationDto()
    {
        var orderId = Guid.NewGuid();
        var converter = new TradingIntegrationDtoConverter();
        var domainEvent = new OrderAcceptedEvent(
            orderId,
            "client-1",
            "strategy-1",
            "market-1",
            "exchange-1",
            "corr-1");

        var payload = Assert.IsType<OrderAcceptedIntegrationEventDto>(converter.Convert(domainEvent));

        Assert.Equal(orderId, payload.OrderId);
        Assert.Equal("client-1", payload.ClientOrderId);
        Assert.Equal("strategy-1", payload.StrategyId);
        Assert.Equal("market-1", payload.MarketId);
        Assert.Equal("exchange-1", payload.ExchangeOrderId);
        Assert.Equal("corr-1", payload.CorrelationId);
        Assert.Equal(TimeSpan.Zero, payload.OccurredAtUtc.Offset);
        Assert.True(converter.CanConvert(domainEvent));
    }

    [Fact]
    public void Convert_ShouldMapAllTradingIntegrationEventsToPureDtos()
    {
        var orderId = Guid.NewGuid();
        var tradingAccountId = Guid.NewGuid();
        var converter = new TradingIntegrationDtoConverter();

        var mappedPayloads = new object[]
        {
            converter.Convert(new OrderCancelledEvent(orderId, "client-1", "strategy-1", "market-1", "corr-1")),
            converter.Convert(new OrderExpiredEvent(orderId, "client-1", "strategy-1", "market-1", "corr-1")),
            converter.Convert(new OrderFilledEvent(orderId, "client-1", "strategy-1", "market-1", 3m, 0.42m, true, "corr-1")),
            converter.Convert(new OrderRejectedEvent(orderId, "client-1", "strategy-1", "market-1", "risk rejected", "corr-1")),
            converter.Convert(new TradeExecutedEvent(
                orderId,
                tradingAccountId,
                "client-1",
                "strategy-1",
                "market-1",
                "token-1",
                OutcomeSide.Yes,
                OrderSide.Buy,
                0.42m,
                3m,
                "trade-1",
                0.01m,
                "corr-1"))
        };

        Assert.IsType<OrderCancelledIntegrationEventDto>(mappedPayloads[0]);
        Assert.IsType<OrderExpiredIntegrationEventDto>(mappedPayloads[1]);

        var filled = Assert.IsType<OrderFilledIntegrationEventDto>(mappedPayloads[2]);
        Assert.True(filled.IsPartial);
        Assert.Equal(3m, filled.FilledQuantity);
        Assert.Equal(0.42m, filled.FillPrice);

        var rejected = Assert.IsType<OrderRejectedIntegrationEventDto>(mappedPayloads[3]);
        Assert.Equal("risk rejected", rejected.RejectReason);

        var trade = Assert.IsType<TradeExecutedIntegrationEventDto>(mappedPayloads[4]);
        Assert.Equal(tradingAccountId, trade.TradingAccountId);
        Assert.Equal("Yes", trade.Outcome);
        Assert.Equal("Buy", trade.Side);
        Assert.Equal(1.26m, trade.Notional);
    }

    [Fact]
    public void Registry_ShouldThrowWhenIntegrationEventHasNoDtoConverter()
    {
        var registry = new IntegrationDtoConverterRegistry(Array.Empty<TradingIntegrationDtoConverter>());
        var domainEvent = new OrderCancelledEvent(
            Guid.NewGuid(),
            "client-1",
            "strategy-1",
            "market-1",
            "corr-1");

        var exception = Assert.Throws<InvalidOperationException>(() => registry.Convert(domainEvent));

        Assert.Contains(nameof(OrderCancelledEvent), exception.Message);
    }
}
