using Autotrade.MarketData.Application.Contract.OrderBook;
using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.Snapshots;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;

namespace Autotrade.Strategy.Application.Contract.Strategies;

/// <summary>
/// Strategy execution context.
/// </summary>
public sealed class StrategyContext
{
    public required string StrategyId { get; init; }

    public required IExecutionService ExecutionService { get; init; }

    public required IOrderBookReader OrderBookReader { get; init; }

    public required IMarketCatalogReader MarketCatalog { get; init; }

    public IMarketDataSnapshotReader? MarketDataSnapshotReader { get; init; }

    public required IRiskManager RiskManager { get; init; }

    public required IStrategyDecisionLogger DecisionLogger { get; init; }

    public IStrategyObservationLogger ObservationLogger { get; init; } = NoopStrategyObservationLogger.Instance;
}
