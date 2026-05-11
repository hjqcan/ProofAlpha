using Autotrade.Strategy.Application.Contract.Strategies;

namespace Autotrade.Strategy.Application.Strategies;

public abstract class TradingStrategyBase : ITradingStrategy
{
    protected TradingStrategyBase(StrategyContext context, string name)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Id = context.StrategyId;
        Name = name;
        State = StrategyState.Created;
    }

    protected StrategyContext Context { get; }

    public string Id { get; }

    public string Name { get; }

    public StrategyState State { get; private set; }

    public virtual Task StartAsync(CancellationToken cancellationToken = default)
    {
        State = StrategyState.Running;
        return Task.CompletedTask;
    }

    public virtual Task PauseAsync(CancellationToken cancellationToken = default)
    {
        State = StrategyState.Paused;
        return Task.CompletedTask;
    }

    public virtual Task StopAsync(CancellationToken cancellationToken = default)
    {
        State = StrategyState.Stopped;
        return Task.CompletedTask;
    }

    public abstract Task<IEnumerable<string>> SelectMarketsAsync(CancellationToken cancellationToken = default);

    public abstract Task<StrategySignal?> EvaluateEntryAsync(MarketSnapshot snapshot,
        CancellationToken cancellationToken = default);

    public abstract Task<StrategySignal?> EvaluateExitAsync(MarketSnapshot snapshot,
        CancellationToken cancellationToken = default);

    public abstract Task OnOrderUpdateAsync(StrategyOrderUpdate update,
        CancellationToken cancellationToken = default);
}
