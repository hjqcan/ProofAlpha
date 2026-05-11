namespace Autotrade.Strategy.Application.Strategies.RepricingLag;

public enum RepricingLagState
{
    Wait = 0,
    Confirm = 1,
    Signal = 2,
    Submit = 3,
    Monitor = 4,
    Exit = 5,
    Faulted = 6
}
