using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Trading.Application.Contract.Risk;

/// <summary>
/// Risk evaluation input for a new order.
/// </summary>
public sealed record RiskOrderRequest
{
    public required string StrategyId { get; init; }

    public required string ClientOrderId { get; init; }

    public required string MarketId { get; init; }

    public required string TokenId { get; init; }

    public required OrderSide Side { get; init; }

    public required OrderType OrderType { get; init; }

    public required TimeInForce TimeInForce { get; init; }

    public required decimal Price { get; init; }

    public required decimal Quantity { get; init; }

    public OrderLeg Leg { get; init; } = OrderLeg.Single;

    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    public decimal Notional => Price * Quantity;
}
