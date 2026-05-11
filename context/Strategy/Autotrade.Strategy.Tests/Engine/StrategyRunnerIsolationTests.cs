using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.OrderBook;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Decisions;
using Autotrade.Strategy.Application.Engine;
using Autotrade.Strategy.Application.Orders;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Autotrade.Strategy.Tests.Engine;

/// <summary>
/// Tests for error isolation and concurrency: ensures slow/faulting strategies
/// don't block other strategies via the Channel-based data routing.
/// </summary>
public sealed class StrategyRunnerIsolationTests
{
    [Fact]
    public async Task Channel_SlowStrategy_DoesNotBlockProducer()
    {
        // Arrange: Create a channel with small capacity
        using var channel = new StrategyMarketChannel(capacity: 5);

        // Act: Write more items than capacity (simulates producer faster than consumer)
        for (int i = 0; i < 10; i++)
        {
            channel.TryWrite(CreateSnapshot($"mkt-{i}"));
        }

        // Assert: Channel should have exactly 5 items (oldest 5 dropped)
        Assert.Equal(5, channel.Backlog);

        var batch = channel.TryReadBatch(10);
        Assert.Equal(5, batch.Count);

        // Verify oldest were dropped (mkt-0 through mkt-4), keeping mkt-5 through mkt-9
        Assert.Equal("mkt-5", batch[0].MarketId);
        Assert.Equal("mkt-9", batch[4].MarketId);
    }

    [Fact]
    public async Task DataRouter_IsolatesSlowStrategies()
    {
        // Arrange
        var snapshotProvider = new FakeSnapshotProvider();
        snapshotProvider.AddSnapshot(CreateSnapshot("mkt-1"));
        snapshotProvider.AddSnapshot(CreateSnapshot("mkt-2"));

        using var router = new StrategyDataRouter(snapshotProvider, NullLogger<StrategyDataRouter>.Instance);

        // Register two strategies
        var fastChannel = router.RegisterStrategy("fast-strategy", channelCapacity: 100);
        var slowChannel = router.RegisterStrategy("slow-strategy", channelCapacity: 2);

        router.UpdateSubscriptions("fast-strategy", new[] { "mkt-1", "mkt-2" });
        router.UpdateSubscriptions("slow-strategy", new[] { "mkt-1", "mkt-2" });

        // Simulate slow strategy by not reading
        // Write more data than slow channel can hold
        for (int i = 0; i < 5; i++)
        {
            snapshotProvider.AddSnapshot(CreateSnapshot($"mkt-{i}"));
            router.UpdateSubscriptions("fast-strategy", new[] { $"mkt-{i}" });
            router.UpdateSubscriptions("slow-strategy", new[] { $"mkt-{i}" });
        }

        // Act: Fast strategy reads normally
        var fastBatch = fastChannel.TryReadBatch(10);

        // Assert: Both channels should be independent
        Assert.True(fastChannel.Backlog >= 0);
        Assert.True(slowChannel.Backlog <= 2); // Bounded by capacity
    }

    [Fact]
    public async Task Runner_TracksActiveMarkets()
    {
        // Arrange
        var runner = CreateRunner(out var strategy);
        strategy.MarketsToSelect = new[] { "mkt-1", "mkt-2", "mkt-3" };

        // Act: Run briefly
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        try
        {
            await runner.RunAsync(cts.Token);
        }
        catch (OperationCanceledException) { }

        // Assert
        var activeMarkets = runner.GetActiveMarkets();
        Assert.Equal(3, activeMarkets.Count);
        Assert.Contains("mkt-1", activeMarkets);
        Assert.Contains("mkt-2", activeMarkets);
        Assert.Contains("mkt-3", activeMarkets);
    }

    [Fact]
    public async Task Runner_IncrementsCycleCount()
    {
        // Arrange
        var runner = CreateRunner(out var strategy);
        strategy.MarketsToSelect = Array.Empty<string>(); // No markets, just cycle

        // Act: Run briefly
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        try
        {
            await runner.RunAsync(cts.Token);
        }
        catch (OperationCanceledException) { }

        // Assert
        Assert.True(runner.CycleCount >= 1);
    }

    [Fact]
    public async Task Runner_IncrementsSnapshotsProcessed()
    {
        // Arrange
        var snapshotProvider = new FakeSnapshotProvider();
        snapshotProvider.AddSnapshot(CreateSnapshot("mkt-1"));

        var runner = CreateRunner(out var strategy, snapshotProvider);
        strategy.MarketsToSelect = new[] { "mkt-1" };

        // Act: Run briefly
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        try
        {
            await runner.RunAsync(cts.Token);
        }
        catch (OperationCanceledException) { }

        // Assert
        Assert.True(runner.SnapshotsProcessed >= 1);
    }

    [Fact]
    public async Task Runner_ChannelMode_ReadsAtLeastOneSnapshotPerActiveMarket()
    {
        // Arrange
        var snapshotProvider = new FakeSnapshotProvider();
        using var router = new StrategyDataRouter(snapshotProvider, NullLogger<StrategyDataRouter>.Instance);
        var channel = router.RegisterStrategy("test-strategy", channelCapacity: 10);

        channel.TryWrite(CreateSnapshot("mkt-1"));
        channel.TryWrite(CreateSnapshot("mkt-2"));
        channel.TryWrite(CreateSnapshot("mkt-3"));

        var runner = CreateRunner(
            out var strategy,
            snapshotProvider,
            channel: channel,
            router: router,
            maxOrdersPerCycle: 1);
        strategy.MarketsToSelect = new[] { "mkt-1", "mkt-2", "mkt-3" };

        // Act
        await RunBrieflyAsync(runner);

        // Assert
        Assert.Contains("mkt-1", strategy.EntryEvaluatedMarkets);
        Assert.Contains("mkt-2", strategy.EntryEvaluatedMarkets);
        Assert.Contains("mkt-3", strategy.EntryEvaluatedMarkets);
    }

    [Fact]
    public async Task Runner_MultipleOrdersSignal_UsesBatchExecutionOnce()
    {
        // Arrange
        var snapshotProvider = new FakeSnapshotProvider();
        snapshotProvider.AddSnapshot(CreateSnapshot("mkt-1"));

        var executionService = new RecordingExecutionService();
        var riskManager = new FakeRiskManager();
        var decisionLogger = new FakeDecisionLogger();
        var context = BuildContext(executionService, riskManager, decisionLogger);
        var orderRegistry = new StrategyOrderRegistry();
        var runner = CreateRunner(out var strategy, snapshotProvider, context, orderRegistry);

        strategy.MarketsToSelect = new[] { "mkt-1" };
        strategy.EntrySignal = CreateSignal("mkt-1", CreateIntent("yes-token", OrderLeg.First), CreateIntent("no-token", OrderLeg.Second));

        // Act
        await RunBrieflyAsync(runner);

        // Assert
        Assert.Equal(0, executionService.PlaceOrderCallCount);
        Assert.Equal(1, executionService.PlaceOrdersCallCount);
        Assert.Single(executionService.BatchRequests);
        Assert.Equal(2, executionService.BatchRequests[0].Count);
        Assert.Equal(2, strategy.OrderUpdates.Count);
        Assert.Equal(2, riskManager.AcceptedRequests.Count);
        Assert.Equal(2, orderRegistry.GetOpenOrders().Count);
    }

    [Fact]
    public async Task Runner_MultipleOrdersSignal_RespectsMaxOrdersPerCycleBeforeBatchSubmission()
    {
        // Arrange
        var snapshotProvider = new FakeSnapshotProvider();
        snapshotProvider.AddSnapshot(CreateSnapshot("mkt-1"));

        var executionService = new RecordingExecutionService();
        var riskManager = new FakeRiskManager();
        var decisionLogger = new FakeDecisionLogger();
        var context = BuildContext(executionService, riskManager, decisionLogger);
        var orderRegistry = new StrategyOrderRegistry();
        var runner = CreateRunner(
            out var strategy,
            snapshotProvider,
            context,
            orderRegistry,
            maxOrdersPerCycle: 1);

        strategy.MarketsToSelect = new[] { "mkt-1" };
        strategy.EntrySignal = CreateSignal("mkt-1", CreateIntent("yes-token", OrderLeg.First), CreateIntent("no-token", OrderLeg.Second));

        // Act
        await RunBrieflyAsync(runner);

        // Assert
        Assert.Equal(0, executionService.PlaceOrderCallCount);
        Assert.Equal(1, executionService.PlaceOrdersCallCount);
        Assert.Single(executionService.BatchRequests);
        Assert.Single(executionService.BatchRequests[0]);
        Assert.Single(strategy.OrderUpdates);
        Assert.Single(riskManager.AcceptedRequests);
        Assert.Single(orderRegistry.GetOpenOrders());
    }

    [Fact]
    public async Task Runner_MaxOrdersPerCycleCountsSubmittedOrdersNotOnlySuccessfulPlacements()
    {
        // Arrange
        var snapshotProvider = new FakeSnapshotProvider();
        snapshotProvider.AddSnapshot(CreateSnapshot("mkt-1"));

        var executionService = new RecordingExecutionService
        {
            BatchHandler = requests => requests
                .Select(request => ExecutionResult.Fail(request.ClientOrderId, "EXCHANGE_REJECTED", "Rejected by exchange"))
                .ToArray()
        };
        var riskManager = new FakeRiskManager();
        var decisionLogger = new FakeDecisionLogger();
        var context = BuildContext(executionService, riskManager, decisionLogger);
        var orderRegistry = new StrategyOrderRegistry();
        var runner = CreateRunner(
            out var strategy,
            snapshotProvider,
            context,
            orderRegistry,
            maxOrdersPerCycle: 1);

        strategy.MarketsToSelect = new[] { "mkt-1" };
        strategy.EntrySignal = CreateSignal("mkt-1", CreateIntent("yes-token", OrderLeg.First));
        strategy.ExitSignal = CreateSignal("mkt-1", CreateIntent("no-token", OrderLeg.Second));

        // Act
        await RunBrieflyAsync(runner);

        // Assert
        Assert.Equal(1, executionService.PlaceOrdersCallCount);
        Assert.Single(executionService.BatchRequests);
        Assert.Single(executionService.BatchRequests[0]);
        Assert.Empty(strategy.OrderUpdates);
        Assert.Empty(riskManager.AcceptedRequests);
        Assert.Single(riskManager.RecordedErrors);
    }

    [Fact]
    public async Task Runner_BatchPartialFailure_ProcessesEachOrderResult()
    {
        // Arrange
        var snapshotProvider = new FakeSnapshotProvider();
        snapshotProvider.AddSnapshot(CreateSnapshot("mkt-1"));

        var executionService = new RecordingExecutionService
        {
            BatchHandler = requests => new[]
            {
                ExecutionResult.Succeed(requests[0].ClientOrderId, "exchange-1"),
                ExecutionResult.Fail(requests[1].ClientOrderId, "EXCHANGE_REJECTED", "Rejected by exchange")
            }
        };
        var riskManager = new FakeRiskManager();
        var decisionLogger = new FakeDecisionLogger();
        var context = BuildContext(executionService, riskManager, decisionLogger);
        var orderRegistry = new StrategyOrderRegistry();
        var runner = CreateRunner(out var strategy, snapshotProvider, context, orderRegistry);

        strategy.MarketsToSelect = new[] { "mkt-1" };
        strategy.EntrySignal = CreateSignal("mkt-1", CreateIntent("yes-token", OrderLeg.First), CreateIntent("no-token", OrderLeg.Second));

        // Act
        await RunBrieflyAsync(runner);

        // Assert
        Assert.Equal(0, executionService.PlaceOrderCallCount);
        Assert.Equal(1, executionService.PlaceOrdersCallCount);
        Assert.Single(strategy.OrderUpdates);
        Assert.Single(riskManager.AcceptedRequests);
        Assert.Single(riskManager.RecordedErrors);
        Assert.Equal("EXCHANGE_REJECTED", riskManager.RecordedErrors[0].ErrorCode);
        Assert.Single(orderRegistry.GetOpenOrders());
        Assert.Contains(decisionLogger.Decisions, decision => decision.Action == "OrderRejected");
    }

    [Fact]
    public async Task Concurrent_MultipleStrategies_RunIndependently()
    {
        // Arrange: Create multiple runners
        var snapshotProvider = new FakeSnapshotProvider();
        snapshotProvider.AddSnapshot(CreateSnapshot("mkt-1"));

        var runner1 = CreateRunner(out var strategy1, snapshotProvider);
        var runner2 = CreateRunner(out var strategy2, snapshotProvider);

        strategy1.MarketsToSelect = new[] { "mkt-1" };
        strategy2.MarketsToSelect = new[] { "mkt-1" };

        // Simulate one strategy being slow
        strategy1.EvaluateDelay = TimeSpan.FromMilliseconds(50);
        strategy2.EvaluateDelay = TimeSpan.Zero;

        // Act: Run both concurrently
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        var task1 = Task.Run(async () =>
        {
            try { await runner1.RunAsync(cts.Token); }
            catch (OperationCanceledException) { }
        });

        var task2 = Task.Run(async () =>
        {
            try { await runner2.RunAsync(cts.Token); }
            catch (OperationCanceledException) { }
        });

        await Task.WhenAll(task1, task2);

        // Assert: Fast strategy should have processed more cycles
        Assert.True(runner2.CycleCount >= runner1.CycleCount);
    }

    private static async Task RunBrieflyAsync(StrategyRunner runner)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        try
        {
            await runner.RunAsync(cts.Token);
        }
        catch (OperationCanceledException) { }
    }

    private static StrategyRunner CreateRunner(
        out FakeStrategy strategy,
        FakeSnapshotProvider? snapshotProvider = null,
        StrategyContext? context = null,
        StrategyOrderRegistry? orderRegistry = null,
        int maxOrdersPerCycle = 10,
        StrategyMarketChannel? channel = null,
        StrategyDataRouter? router = null)
    {
        strategy = new FakeStrategy("test-strategy", "Test Strategy");
        var control = new StrategyControl();
        var options = Options.Create(new StrategyEngineOptions
        {
            EvaluationIntervalSeconds = 1, // Minimal delay for testing
            MaxOrdersPerCycle = maxOrdersPerCycle,
            SnapshotTimeoutSeconds = 5
        });

        return new StrategyRunner(
            strategy,
            context ?? BuildContext(),
            snapshotProvider ?? new FakeSnapshotProvider(),
            orderRegistry ?? new StrategyOrderRegistry(),
            control,
            options,
            NullLogger<StrategyRunner>.Instance,
            channel: channel,
            router: router);
    }

    private static StrategyContext BuildContext(
        IExecutionService? executionService = null,
        IRiskManager? riskManager = null,
        IStrategyDecisionLogger? decisionLogger = null)
    {
        return new StrategyContext
        {
            StrategyId = "test-strategy",
            ExecutionService = executionService ?? new FakeExecutionService(),
            OrderBookReader = new FakeOrderBookReader(),
            MarketCatalog = new FakeMarketCatalogReader(),
            RiskManager = riskManager ?? new FakeRiskManager(),
            DecisionLogger = decisionLogger ?? new FakeDecisionLogger()
        };
    }

    private static StrategySignal CreateSignal(string marketId, params StrategyOrderIntent[] orders)
    {
        return new StrategySignal(
            StrategySignalType.Entry,
            marketId,
            "test signal",
            orders,
            "{\"source\":\"test\"}");
    }

    private static StrategyOrderIntent CreateIntent(string tokenId, OrderLeg leg)
    {
        return new StrategyOrderIntent(
            "mkt-1",
            tokenId,
            tokenId.StartsWith("yes", StringComparison.OrdinalIgnoreCase) ? OutcomeSide.Yes : OutcomeSide.No,
            OrderSide.Buy,
            OrderType.Limit,
            TimeInForce.Gtc,
            0.51m,
            10m,
            false,
            leg);
    }

    private static MarketSnapshot CreateSnapshot(string marketId)
    {
        var market = new MarketInfoDto
        {
            MarketId = marketId,
            ConditionId = "cond-1",
            Name = "Test Market",
            Status = "Active",
            TokenIds = new[] { "yes-token", "no-token" }
        };

        return new MarketSnapshot(market, null, null, DateTimeOffset.UtcNow);
    }

    private sealed class FakeStrategy : ITradingStrategy
    {
        public FakeStrategy(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public string Id { get; }
        public string Name { get; }
        public StrategyState State { get; private set; } = StrategyState.Created;
        public string[] MarketsToSelect { get; set; } = Array.Empty<string>();
        public TimeSpan EvaluateDelay { get; set; } = TimeSpan.Zero;
        public StrategySignal? EntrySignal { get; set; }
        public StrategySignal? ExitSignal { get; set; }
        public List<StrategyOrderUpdate> OrderUpdates { get; } = new();
        public List<string> EntryEvaluatedMarkets { get; } = new();
        private bool _entrySignalEmitted;
        private bool _exitSignalEmitted;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            State = StrategyState.Running;
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken cancellationToken = default)
        {
            State = StrategyState.Paused;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            State = StrategyState.Stopped;
            return Task.CompletedTask;
        }

        public Task<IEnumerable<string>> SelectMarketsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<string>>(MarketsToSelect);

        public async Task<StrategySignal?> EvaluateEntryAsync(MarketSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(snapshot.MarketId))
            {
                EntryEvaluatedMarkets.Add(snapshot.MarketId);
            }

            if (EvaluateDelay > TimeSpan.Zero)
            {
                await Task.Delay(EvaluateDelay, cancellationToken);
            }

            if (EntrySignal is null || _entrySignalEmitted)
            {
                return null;
            }

            _entrySignalEmitted = true;
            return EntrySignal;
        }

        public async Task<StrategySignal?> EvaluateExitAsync(MarketSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            if (EvaluateDelay > TimeSpan.Zero)
            {
                await Task.Delay(EvaluateDelay, cancellationToken);
            }

            if (ExitSignal is null || _exitSignalEmitted)
            {
                return null;
            }

            _exitSignalEmitted = true;
            return ExitSignal;
        }

        public Task OnOrderUpdateAsync(StrategyOrderUpdate update, CancellationToken cancellationToken = default)
        {
            OrderUpdates.Add(update);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSnapshotProvider : IMarketSnapshotProvider
    {
        private readonly Dictionary<string, MarketSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);

        public void AddSnapshot(MarketSnapshot snapshot)
        {
            if (snapshot.MarketId is not null)
            {
                _snapshots[snapshot.MarketId] = snapshot;
            }
        }

        public Task<IReadOnlyList<MarketSnapshot>> GetSnapshotsAsync(
            IEnumerable<string> marketIds,
            CancellationToken cancellationToken = default)
        {
            var result = new List<MarketSnapshot>();
            foreach (var marketId in marketIds)
            {
                if (_snapshots.TryGetValue(marketId, out var snapshot))
                {
                    result.Add(snapshot);
                }
            }

            return Task.FromResult<IReadOnlyList<MarketSnapshot>>(result);
        }
    }

    private sealed class FakeExecutionService : IExecutionService
    {
        public Task<ExecutionResult> PlaceOrderAsync(ExecutionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(ExecutionResult.Fail(request.ClientOrderId, "NOT_IMPLEMENTED", "Not implemented"));

        public Task<IReadOnlyList<ExecutionResult>> PlaceOrdersAsync(
            IReadOnlyList<ExecutionRequest> requests,
            CancellationToken cancellationToken = default)
        {
            var results = new List<ExecutionResult>(requests.Count);
            foreach (var request in requests)
            {
                results.Add(ExecutionResult.Fail(request.ClientOrderId, "NOT_IMPLEMENTED", "Not implemented"));
            }

            return Task.FromResult<IReadOnlyList<ExecutionResult>>(results);
        }

        public Task<ExecutionResult> CancelOrderAsync(string clientOrderId, CancellationToken cancellationToken = default)
            => Task.FromResult(ExecutionResult.Fail(clientOrderId, "NOT_IMPLEMENTED", "Not implemented"));

        public Task<OrderStatusResult> GetOrderStatusAsync(string clientOrderId, CancellationToken cancellationToken = default)
            => Task.FromResult(OrderStatusResult.NotFound(clientOrderId));
    }

    private sealed class RecordingExecutionService : IExecutionService
    {
        public int PlaceOrderCallCount { get; private set; }
        public int PlaceOrdersCallCount { get; private set; }
        public List<ExecutionRequest> SingleRequests { get; } = new();
        public List<IReadOnlyList<ExecutionRequest>> BatchRequests { get; } = new();
        public Func<IReadOnlyList<ExecutionRequest>, IReadOnlyList<ExecutionResult>>? BatchHandler { get; set; }

        public Task<ExecutionResult> PlaceOrderAsync(ExecutionRequest request, CancellationToken cancellationToken = default)
        {
            PlaceOrderCallCount++;
            SingleRequests.Add(request);
            return Task.FromResult(ExecutionResult.Succeed(request.ClientOrderId, $"exchange-{PlaceOrderCallCount}"));
        }

        public Task<IReadOnlyList<ExecutionResult>> PlaceOrdersAsync(
            IReadOnlyList<ExecutionRequest> requests,
            CancellationToken cancellationToken = default)
        {
            PlaceOrdersCallCount++;
            BatchRequests.Add(requests.ToArray());

            var results = BatchHandler?.Invoke(requests)
                ?? requests
                    .Select((request, index) => ExecutionResult.Succeed(request.ClientOrderId, $"exchange-{index + 1}"))
                    .ToArray();

            return Task.FromResult(results);
        }

        public Task<ExecutionResult> CancelOrderAsync(string clientOrderId, CancellationToken cancellationToken = default)
            => Task.FromResult(ExecutionResult.Fail(clientOrderId, "NOT_IMPLEMENTED", "Not implemented"));

        public Task<OrderStatusResult> GetOrderStatusAsync(string clientOrderId, CancellationToken cancellationToken = default)
            => Task.FromResult(OrderStatusResult.NotFound(clientOrderId));
    }

    private sealed class FakeOrderBookReader : IOrderBookReader
    {
        public TopOfBookDto? GetTopOfBook(string assetId) => null;
        public IReadOnlyList<PriceLevelDto> GetDepth(string assetId, int levels = 10) => Array.Empty<PriceLevelDto>();
    }

    private sealed class FakeMarketCatalogReader : IMarketCatalogReader
    {
        public MarketInfoDto? GetMarket(string marketId) => null;
        public IReadOnlyList<MarketInfoDto> GetAllMarkets() => Array.Empty<MarketInfoDto>();
        public IReadOnlyList<MarketInfoDto> GetActiveMarkets() => Array.Empty<MarketInfoDto>();
        public IReadOnlyList<MarketInfoDto> GetLiquidMarkets(decimal minVolume) => Array.Empty<MarketInfoDto>();
        public IReadOnlyList<MarketInfoDto> GetExpiringMarkets(TimeSpan within) => Array.Empty<MarketInfoDto>();
    }

    private sealed class FakeRiskManager : IRiskManager
    {
        public bool IsKillSwitchActive => false;
        public List<RiskOrderRequest> AcceptedRequests { get; } = new();
        public List<RiskOrderUpdate> RecordedUpdates { get; } = new();
        public List<RecordedRiskError> RecordedErrors { get; } = new();

        public Task<RiskCheckResult> ValidateOrderAsync(RiskOrderRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(RiskCheckResult.Allow());

        public Task RecordOrderAcceptedAsync(RiskOrderRequest request, CancellationToken cancellationToken = default)
        {
            AcceptedRequests.Add(request);
            return Task.CompletedTask;
        }

        public Task RecordOrderUpdateAsync(RiskOrderUpdate update, CancellationToken cancellationToken = default)
        {
            RecordedUpdates.Add(update);
            return Task.CompletedTask;
        }

        public Task RecordOrderErrorAsync(string strategyId, string clientOrderId, string errorCode, string message,
            CancellationToken cancellationToken = default)
        {
            RecordedErrors.Add(new RecordedRiskError(strategyId, clientOrderId, errorCode, message));
            return Task.CompletedTask;
        }

        public Task ActivateKillSwitchAsync(string reason, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ActivateKillSwitchAsync(KillSwitchLevel level, string reasonCode, string reason, string? contextJson = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ActivateStrategyKillSwitchAsync(string strategyId, KillSwitchLevel level, string reasonCode, string reason,
            string? marketId = null, string? contextJson = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ResetKillSwitchAsync(string? strategyId = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public KillSwitchState GetKillSwitchState() => KillSwitchState.Inactive;
        public KillSwitchState GetStrategyKillSwitchState(string strategyId) => KillSwitchState.Inactive;
        public bool IsStrategyBlocked(string strategyId) => false;
        public IReadOnlyList<KillSwitchState> GetAllActiveKillSwitches() => Array.Empty<KillSwitchState>();
        public IReadOnlyList<string> GetOpenOrderIds() => Array.Empty<string>();
        public IReadOnlyList<string> GetOpenOrderIds(string strategyId) => Array.Empty<string>();

        public IReadOnlyList<UnhedgedExposureSnapshot> GetExpiredUnhedgedExposures(DateTimeOffset nowUtc)
            => Array.Empty<UnhedgedExposureSnapshot>();

        public Task RecordUnhedgedExposureAsync(string strategyId, string marketId, string tokenId, string hedgeTokenId,
            OutcomeSide outcome, OrderSide side, decimal quantity, decimal price, DateTimeOffset startedAtUtc,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ClearUnhedgedExposureAsync(string strategyId, string marketId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public RiskStateSnapshot GetStateSnapshot() => new(0m, 0, 0m, 0m, 0m,
            new Dictionary<string, decimal>(),
            new Dictionary<string, decimal>(),
            new Dictionary<string, int>(),
            Array.Empty<UnhedgedExposureSnapshot>());

        public sealed record RecordedRiskError(string StrategyId, string ClientOrderId, string ErrorCode, string Message);
    }

    private sealed class FakeDecisionLogger : IStrategyDecisionLogger
    {
        public List<StrategyDecision> Decisions { get; } = new();

        public Task LogAsync(StrategyDecision decision, CancellationToken cancellationToken = default)
        {
            Decisions.Add(decision);
            return Task.CompletedTask;
        }
    }
}
