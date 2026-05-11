namespace Autotrade.Strategy.Application.Contract.Strategies;

/// <summary>
/// Decision logger for strategy actions.
/// </summary>
public interface IStrategyDecisionLogger
{
    Task LogAsync(StrategyDecision decision, CancellationToken cancellationToken = default);
}
