using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Strategy.Application.Contract.Strategies;

/// <summary>
/// Order update forwarded to strategies.
/// </summary>
public sealed record StrategyOrderUpdate(
    string StrategyId,
    string ClientOrderId,
    string MarketId,
    string TokenId,
    OutcomeSide Outcome,
    OrderLeg Leg,
    StrategySignalType SignalType,
    OrderSide Side,
    OrderType OrderType,
    TimeInForce TimeInForce,
    decimal Price,
    ExecutionStatus Status,
    decimal FilledQuantity,
    decimal OriginalQuantity,
    DateTimeOffset TimestampUtc);
