namespace Autotrade.Strategy.Application.Contract.Strategies;

/// <summary>
/// Persists structured strategy observations used by the self-improvement loop.
/// </summary>
public interface IStrategyObservationLogger
{
    Task LogAsync(StrategyObservation observation, CancellationToken cancellationToken = default);

    Task FlushAsync(CancellationToken cancellationToken = default);
}
