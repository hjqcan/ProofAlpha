namespace Autotrade.Trading.Application.Contract.Risk;

/// <summary>
/// Order leg semantics for multi-leg strategies.
/// </summary>
public enum OrderLeg
{
    Single = 0,
    First = 1,
    Second = 2
}
