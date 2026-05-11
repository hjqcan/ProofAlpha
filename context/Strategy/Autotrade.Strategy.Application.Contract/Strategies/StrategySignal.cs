using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Strategy.Application.Contract.Strategies;

public enum StrategySignalType
{
    Entry = 0,
    Exit = 1
}

/// <summary>
/// Strategy signal containing order intents.
/// </summary>
public sealed record StrategySignal(
    StrategySignalType Type,
    string MarketId,
    string Reason,
    IReadOnlyList<StrategyOrderIntent> Orders,
    string? ContextJson = null);

public sealed record StrategyOrderIntent(
    string MarketId,
    string TokenId,
    OutcomeSide Outcome,
    OrderSide Side,
    OrderType OrderType,
    TimeInForce TimeInForce,
    decimal Price,
    decimal Quantity,
    bool NegRisk,
    OrderLeg Leg);
