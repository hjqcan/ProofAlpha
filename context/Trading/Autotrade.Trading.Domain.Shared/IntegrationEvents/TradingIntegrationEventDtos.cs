namespace Autotrade.Trading.Domain.Shared.IntegrationEvents;

/// <summary>
/// Pure DTO payloads published across bounded-context boundaries.
/// </summary>
public sealed record OrderAcceptedIntegrationEventDto(
    Guid OrderId,
    string ClientOrderId,
    string StrategyId,
    string MarketId,
    string ExchangeOrderId,
    string? CorrelationId,
    DateTimeOffset OccurredAtUtc)
{
    public const string EventName = "Trading.Order.Accepted";
    public const string Version = "v1";
}

public sealed record OrderCancelledIntegrationEventDto(
    Guid OrderId,
    string ClientOrderId,
    string StrategyId,
    string MarketId,
    string? CorrelationId,
    DateTimeOffset OccurredAtUtc)
{
    public const string EventName = "Trading.Order.Cancelled";
    public const string Version = "v1";
}

public sealed record OrderExpiredIntegrationEventDto(
    Guid OrderId,
    string ClientOrderId,
    string StrategyId,
    string MarketId,
    string? CorrelationId,
    DateTimeOffset OccurredAtUtc)
{
    public const string EventName = "Trading.Order.Expired";
    public const string Version = "v1";
}

public sealed record OrderFilledIntegrationEventDto(
    Guid OrderId,
    string ClientOrderId,
    string StrategyId,
    string MarketId,
    decimal FilledQuantity,
    decimal FillPrice,
    bool IsPartial,
    string? CorrelationId,
    DateTimeOffset OccurredAtUtc)
{
    public const string FilledEventName = "Trading.Order.Filled";
    public const string PartiallyFilledEventName = "Trading.Order.PartiallyFilled";
    public const string Version = "v1";
}

public sealed record OrderRejectedIntegrationEventDto(
    Guid OrderId,
    string ClientOrderId,
    string StrategyId,
    string MarketId,
    string RejectReason,
    string? CorrelationId,
    DateTimeOffset OccurredAtUtc)
{
    public const string EventName = "Trading.Order.Rejected";
    public const string Version = "v1";
}

public sealed record TradeExecutedIntegrationEventDto(
    Guid OrderId,
    Guid TradingAccountId,
    string ClientOrderId,
    string StrategyId,
    string MarketId,
    string TokenId,
    string Outcome,
    string Side,
    decimal Price,
    decimal Quantity,
    decimal Notional,
    string ExchangeTradeId,
    decimal Fee,
    string? CorrelationId,
    DateTimeOffset OccurredAtUtc)
{
    public const string EventName = "Trading.Trade.Executed";
    public const string Version = "v1";
}
