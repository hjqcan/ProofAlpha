namespace Autotrade.Strategy.Application.Contract.Strategies;

/// <summary>
/// Strategy lifecycle state.
/// </summary>
public enum StrategyState
{
    Created = 0,
    Running = 1,
    Paused = 2,
    Stopped = 3,
    Faulted = 4
}
