namespace Autotrade.Strategy.Application.Contract.Strategies;

/// <summary>
/// Strategy contract.
/// </summary>
public interface ITradingStrategy
{
    string Id { get; }

    string Name { get; }

    StrategyState State { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task PauseAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task<IEnumerable<string>> SelectMarketsAsync(CancellationToken cancellationToken = default);

    Task<StrategySignal?> EvaluateEntryAsync(MarketSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<StrategySignal?> EvaluateExitAsync(MarketSnapshot snapshot, CancellationToken cancellationToken = default);

    Task OnOrderUpdateAsync(StrategyOrderUpdate update, CancellationToken cancellationToken = default);
}
