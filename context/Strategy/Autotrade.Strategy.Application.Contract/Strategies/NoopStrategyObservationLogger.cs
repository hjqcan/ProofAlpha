namespace Autotrade.Strategy.Application.Contract.Strategies;

public sealed class NoopStrategyObservationLogger : IStrategyObservationLogger
{
    public static NoopStrategyObservationLogger Instance { get; } = new();

    private NoopStrategyObservationLogger()
    {
    }

    public Task LogAsync(StrategyObservation observation, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
