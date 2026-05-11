using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.OrderBook;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Decisions;
using Autotrade.Strategy.Application.Engine;
using Autotrade.Strategy.Application.Orders;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Autotrade.Strategy.Tests.Engine;

public sealed class StrategySupervisorTests
{
    [Fact]
    public async Task StartAsync_SetsStateToRunning()
    {
        var (supervisor, statuses) = CreateSupervisor();

        await supervisor.StartAsync(CancellationToken.None);

        Assert.Equal(StrategyState.Running, supervisor.State);
        Assert.Contains(statuses, s => s.State == StrategyState.Running);
    }

    [Fact]
    public async Task PauseAsync_SetsStateToPaused()
    {
        var (supervisor, statuses) = CreateSupervisor();

        await supervisor.StartAsync(CancellationToken.None);
        await supervisor.PauseAsync(CancellationToken.None);

        Assert.Equal(StrategyState.Paused, supervisor.State);
        Assert.Contains(statuses, s => s.State == StrategyState.Paused);
    }

    [Fact]
    public async Task ResumeAsync_SetsStateToRunning_AfterPause()
    {
        var (supervisor, statuses) = CreateSupervisor();

        await supervisor.StartAsync(CancellationToken.None);
        await supervisor.PauseAsync(CancellationToken.None);
        await supervisor.ResumeAsync(CancellationToken.None);

        Assert.Equal(StrategyState.Running, supervisor.State);
    }

    [Fact]
    public async Task StopAsync_SetsStateToStopped()
    {
        var (supervisor, statuses) = CreateSupervisor();

        await supervisor.StartAsync(CancellationToken.None);
        await Task.Delay(100); // Let the runner start
        await supervisor.StopAsync(CancellationToken.None);

        Assert.Equal(StrategyState.Stopped, supervisor.State);
        Assert.Contains(statuses, s => s.State == StrategyState.Stopped);
    }

    [Fact]
    public async Task NotifyStatsAsync_UpdatesStatusWithActiveMarkets()
    {
        var (supervisor, statuses) = CreateSupervisor();

        await supervisor.StartAsync(CancellationToken.None);
        await supervisor.NotifyStatsAsync(new[] { "mkt-1", "mkt-2" }, 10, 50);

        var latestStatus = statuses.Last();
        Assert.NotNull(latestStatus.ActiveMarkets);
        Assert.Equal(2, latestStatus.ActiveMarkets!.Count);
        Assert.Equal(10, latestStatus.CycleCount);
        Assert.Equal(50, latestStatus.SnapshotsProcessed);
    }

    [Fact]
    public async Task NotifyHeartbeatAsync_UpdatesLastHeartbeat()
    {
        var (supervisor, statuses) = CreateSupervisor();

        await supervisor.StartAsync(CancellationToken.None);
        var timestamp = DateTimeOffset.UtcNow;
        await supervisor.NotifyHeartbeatAsync(timestamp);

        var latestStatus = statuses.Last();
        Assert.NotNull(latestStatus.LastHeartbeatUtc);
        Assert.Equal(timestamp, latestStatus.LastHeartbeatUtc);
    }

    [Fact]
    public async Task NotifyDecisionAsync_UpdatesLastDecision()
    {
        var (supervisor, statuses) = CreateSupervisor();

        await supervisor.StartAsync(CancellationToken.None);
        var timestamp = DateTimeOffset.UtcNow;
        await supervisor.NotifyDecisionAsync(timestamp);

        var latestStatus = statuses.Last();
        Assert.NotNull(latestStatus.LastDecisionAtUtc);
        Assert.Equal(timestamp, latestStatus.LastDecisionAtUtc);
    }

    [Fact]
    public async Task StartAsync_WhenKillSwitchBlocked_PublishesBlockedReason()
    {
        var (supervisor, statuses) = CreateSupervisor(isBlocked: () => true);

        await supervisor.StartAsync(CancellationToken.None);

        var latestStatus = statuses.Last();
        Assert.True(latestStatus.IsKillSwitchBlocked);
        Assert.NotNull(latestStatus.BlockedReason);
        Assert.Equal(StrategyBlockedReasonKind.KillSwitch, latestStatus.BlockedReason!.Kind);
        Assert.Equal("kill_switch", latestStatus.BlockedReason.Code);
    }

    [Fact]
    public async Task StartAsync_WhenStrategyDisabled_PublishesDisabledConfigBlockedReason()
    {
        var (supervisor, statuses) = CreateSupervisor(descriptorEnabled: false);

        await supervisor.StartAsync(CancellationToken.None);

        var latestStatus = statuses.Last();
        Assert.False(latestStatus.Enabled);
        Assert.NotNull(latestStatus.BlockedReason);
        Assert.Equal(StrategyBlockedReasonKind.DisabledConfig, latestStatus.BlockedReason!.Kind);
        Assert.Equal("disabled_config", latestStatus.BlockedReason.Code);
    }

    private static (StrategySupervisor supervisor, List<StrategyStatus> statuses) CreateSupervisor(
        bool descriptorEnabled = true,
        Func<bool>? isBlocked = null)
    {
        var descriptor = new StrategyDescriptor("test-strategy", "Test Strategy", descriptorEnabled, "v1", "TestOptions");
        var context = BuildContext();
        var strategy = new FakeStrategy("test-strategy", "Test Strategy");
        var control = new StrategyControl();
        var orderRegistry = new StrategyOrderRegistry();
        var options = Options.Create(new StrategyEngineOptions
        {
            EvaluationIntervalSeconds = 1,
            MaxRestartAttempts = 3,
            RestartDelaySeconds = 1
        });
        var snapshotProvider = new FakeSnapshotProvider();

        var runner = new StrategyRunner(
            strategy,
            context,
            snapshotProvider,
            orderRegistry,
            control,
            options,
            NullLogger<StrategyRunner>.Instance);

        var statuses = new List<StrategyStatus>();
        var scope = new FakeServiceScope();

        var supervisor = new StrategySupervisor(
            descriptor,
            strategy,
            runner,
            control,
            options,
            NullLogger<StrategySupervisor>.Instance,
            status =>
            {
                statuses.Add(status);
                return Task.CompletedTask;
            },
            scope,
            isBlocked: isBlocked);

        return (supervisor, statuses);
    }

    private static StrategyContext BuildContext()
    {
        return new StrategyContext
        {
            StrategyId = "test-strategy",
            ExecutionService = new FakeExecutionService(),
            OrderBookReader = new FakeOrderBookReader(),
            MarketCatalog = new FakeMarketCatalogReader(),
            RiskManager = new FakeRiskManager(),
            DecisionLogger = new FakeDecisionLogger()
        };
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
            => Task.FromResult<IEnumerable<string>>(Array.Empty<string>());

        public Task<StrategySignal?> EvaluateEntryAsync(MarketSnapshot snapshot, CancellationToken cancellationToken = default)
            => Task.FromResult<StrategySignal?>(null);

        public Task<StrategySignal?> EvaluateExitAsync(MarketSnapshot snapshot, CancellationToken cancellationToken = default)
            => Task.FromResult<StrategySignal?>(null);

        public Task OnOrderUpdateAsync(StrategyOrderUpdate update, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeSnapshotProvider : IMarketSnapshotProvider
    {
        public Task<IReadOnlyList<MarketSnapshot>> GetSnapshotsAsync(
            IEnumerable<string> marketIds,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MarketSnapshot>>(Array.Empty<MarketSnapshot>());
    }

    private sealed class FakeServiceScope : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = new FakeServiceProvider();
        public void Dispose() { }
    }

    private sealed class FakeServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
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

        public Task<RiskCheckResult> ValidateOrderAsync(RiskOrderRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(RiskCheckResult.Allow());

        public Task RecordOrderAcceptedAsync(RiskOrderRequest request, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordOrderUpdateAsync(RiskOrderUpdate update, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordOrderErrorAsync(string strategyId, string clientOrderId, string errorCode, string message,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

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
    }

    private sealed class FakeDecisionLogger : IStrategyDecisionLogger
    {
        public Task LogAsync(StrategyDecision decision, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
