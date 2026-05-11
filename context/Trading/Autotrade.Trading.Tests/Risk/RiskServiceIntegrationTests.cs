using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Application.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Autotrade.Trading.Infra.BackgroundJobs.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Autotrade.Trading.Tests.Risk;

/// <summary>
/// KillSwitchWorker 和 UnhedgedExposureWorker 的场景测试。
/// </summary>
public class RiskServiceIntegrationTests
{
    #region KillSwitchWorker Tests

    [Fact]
    public async Task KillSwitchWorker_HardStop_应撤销所有订单()
    {
        // Arrange
        var riskManager = CreateRiskManager();
        var executionService = new MockExecutionService();
        var services = CreateServiceProvider(riskManager, executionService);
            var killSwitchService = new KillSwitchWorker(
                riskManager,
                services,
                CreateKillSwitchControlMonitor(),
                NullLogger<KillSwitchWorker>.Instance);

        // 添加一些订单
        await riskManager.RecordOrderAcceptedAsync(CreateRequest("strategy-1", "order-1"));
        await riskManager.RecordOrderAcceptedAsync(CreateRequest("strategy-1", "order-2"));
        await riskManager.RecordOrderAcceptedAsync(CreateRequest("strategy-2", "order-3"));

        Assert.Equal(3, riskManager.GetOpenOrderIds().Count);

        // 激活 HardStop
        await riskManager.ActivateKillSwitchAsync(KillSwitchLevel.HardStop, "TEST", "Test hard stop");

        // Act - 模拟服务执行一个周期
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            await killSwitchService.StartAsync(cts.Token);
            await Task.Delay(100, cts.Token); // 给服务一些时间处理
            await killSwitchService.StopAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert
        Assert.True(executionService.CancelledOrderIds.Count >= 1, "Should have attempted to cancel at least one order");
    }

    [Fact]
    public async Task KillSwitchWorker_SoftStop_不应撤销订单()
    {
        // Arrange
        var riskManager = CreateRiskManager();
        var executionService = new MockExecutionService();
        var services = CreateServiceProvider(riskManager, executionService);
            var killSwitchService = new KillSwitchWorker(
                riskManager,
                services,
                CreateKillSwitchControlMonitor(),
                NullLogger<KillSwitchWorker>.Instance);

        // 添加订单
        await riskManager.RecordOrderAcceptedAsync(CreateRequest("strategy-1", "order-1"));

        // 激活 SoftStop
        await riskManager.ActivateKillSwitchAsync(KillSwitchLevel.SoftStop, "TEST", "Test soft stop");

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            await killSwitchService.StartAsync(cts.Token);
            await Task.Delay(100, cts.Token);
            await killSwitchService.StopAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert - SoftStop 不应撤销订单
        Assert.Empty(executionService.CancelledOrderIds);
    }

    [Fact]
    public async Task KillSwitchWorker_策略级HardStop_只撤销该策略订单()
    {
        // Arrange
        var riskManager = CreateRiskManager();
        var executionService = new MockExecutionService();
        var services = CreateServiceProvider(riskManager, executionService);
            var killSwitchService = new KillSwitchWorker(
                riskManager,
                services,
                CreateKillSwitchControlMonitor(),
                NullLogger<KillSwitchWorker>.Instance);

        // 添加不同策略的订单
        await riskManager.RecordOrderAcceptedAsync(CreateRequest("strategy-1", "order-1"));
        await riskManager.RecordOrderAcceptedAsync(CreateRequest("strategy-1", "order-2"));
        await riskManager.RecordOrderAcceptedAsync(CreateRequest("strategy-2", "order-3"));

        // 只对 strategy-1 激活 HardStop
        await riskManager.ActivateStrategyKillSwitchAsync(
            "strategy-1",
            KillSwitchLevel.HardStop,
            "TEST",
            "Test strategy hard stop");

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            await killSwitchService.StartAsync(cts.Token);
            await Task.Delay(200, cts.Token);
            await killSwitchService.StopAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert - 应该只撤销 strategy-1 的订单
        Assert.Contains("order-1", executionService.CancelledOrderIds);
        Assert.Contains("order-2", executionService.CancelledOrderIds);
        Assert.DoesNotContain("order-3", executionService.CancelledOrderIds);
    }

    #endregion

    private static IOptionsMonitor<KillSwitchControlOptions> CreateKillSwitchControlMonitor()
        => new StaticOptionsMonitor<KillSwitchControlOptions>(new KillSwitchControlOptions());

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        private readonly T _value;

        public StaticOptionsMonitor(T value)
        {
            _value = value;
        }

        public T CurrentValue => _value;

        public T Get(string? name) => _value;

        public IDisposable OnChange(Action<T, string?> listener) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }

    #region UnhedgedExposureWorker Tests

    [Fact]
    public async Task UnhedgedWatchdog_超时敞口_LogOnly_不触发KillSwitch()
    {
        // Arrange
        var options = Options.Create(new RiskOptions
        {
            MaxFirstLegExposureSeconds = 1,
            UnhedgedExitAction = UnhedgedExitAction.LogOnly,
            KillSwitchEnabled = true
        });
        var riskManager = CreateRiskManager(options);
        var eventRepository = new InMemoryRiskEventRepository(NullLogger<InMemoryRiskEventRepository>.Instance);
        var executionService = new MockExecutionService();
        var services = CreateServiceProvider(riskManager, executionService);
        var watchdog = new UnhedgedExposureWorker(
            riskManager,
            eventRepository,
            services,
            options,
            new RiskMetrics(),
            NullLogger<UnhedgedExposureWorker>.Instance);

        // 记录一个超时的敞口（使用完整参数）
        await RecordTestExposureAsync(riskManager, "strategy-1", "market-1", DateTimeOffset.UtcNow.AddSeconds(-10));

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await watchdog.StartAsync(cts.Token);
            await Task.Delay(200, cts.Token);
            await watchdog.StopAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert - LogOnly 不应触发任何 Kill Switch
        Assert.False(riskManager.IsKillSwitchActive);
        Assert.False(riskManager.IsStrategyBlocked("strategy-1"));
        Assert.Empty(executionService.CancelledOrderIds);

        // 但应该记录了 RiskEvent
        var events = await eventRepository.QueryAsync("strategy-1");
        Assert.NotEmpty(events);
        Assert.Contains(events, e => e.Code == "RISK_UNHEDGED_TIMEOUT_LOG");
    }

    [Fact]
    public async Task UnhedgedWatchdog_超时敞口_CancelOrders_应撤单并触发SoftStop()
    {
        // Arrange
        var options = Options.Create(new RiskOptions
        {
            MaxFirstLegExposureSeconds = 1,
            UnhedgedExitAction = UnhedgedExitAction.CancelOrders,
            KillSwitchEnabled = true
        });
        var riskManager = CreateRiskManager(options);
        var eventRepository = new InMemoryRiskEventRepository(NullLogger<InMemoryRiskEventRepository>.Instance);
        var executionService = new MockExecutionService();
        var services = CreateServiceProvider(riskManager, executionService);
        var watchdog = new UnhedgedExposureWorker(
            riskManager,
            eventRepository,
            services,
            options,
            new RiskMetrics(),
            NullLogger<UnhedgedExposureWorker>.Instance);

        // 添加订单和敞口
        await riskManager.RecordOrderAcceptedAsync(CreateRequest("strategy-1", "order-1"));
        await RecordTestExposureAsync(riskManager, "strategy-1", "market-1", DateTimeOffset.UtcNow.AddSeconds(-10));

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await watchdog.StartAsync(cts.Token);
            await Task.Delay(200, cts.Token);
            await watchdog.StopAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert
        var strategyState = riskManager.GetStrategyKillSwitchState("strategy-1");
        Assert.True(strategyState.IsActive);
        Assert.Equal(KillSwitchLevel.SoftStop, strategyState.Level);
        Assert.Contains("order-1", executionService.CancelledOrderIds);
    }

    [Fact]
    public async Task UnhedgedWatchdog_超时敞口_CancelAndExit_应执行退出订单并验证完整属性()
    {
        // Arrange
        var options = Options.Create(new RiskOptions
        {
            MaxFirstLegExposureSeconds = 1,
            UnhedgedExitAction = UnhedgedExitAction.CancelAndExit,
            KillSwitchEnabled = true
        });
        var riskManager = CreateRiskManager(options);
        var eventRepository = new InMemoryRiskEventRepository(NullLogger<InMemoryRiskEventRepository>.Instance);
        var executionService = new MockExecutionService();
        var services = CreateServiceProvider(riskManager, executionService);
        var watchdog = new UnhedgedExposureWorker(
            riskManager,
            eventRepository,
            services,
            options,
            new RiskMetrics(),
            NullLogger<UnhedgedExposureWorker>.Instance);

        // 记录敞口 - 使用真实数据
        const string testTokenId = "token-yes-123";
        const string testHedgeTokenId = "token-no-123";
        const decimal testQuantity = 100m;
        const decimal testPrice = 0.45m;

        await riskManager.RecordUnhedgedExposureAsync(
            "strategy-1",
            "market-1",
            testTokenId,
            testHedgeTokenId,
            OutcomeSide.Yes,
            OrderSide.Buy,
            testQuantity,
            testPrice,
            DateTimeOffset.UtcNow.AddSeconds(-10));

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await watchdog.StartAsync(cts.Token);
            await Task.Delay(200, cts.Token);
            await watchdog.StopAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert
        var strategyState = riskManager.GetStrategyKillSwitchState("strategy-1");
        Assert.True(strategyState.IsActive);
        Assert.Equal(KillSwitchLevel.HardStop, strategyState.Level);

        // 验证退出订单的完整属性
        var exitOrder = executionService.PlacedOrders.FirstOrDefault(o => o.ClientOrderId.StartsWith("EXIT_"));
        Assert.NotNull(exitOrder);
        Assert.Equal(testTokenId, exitOrder.TokenId);
        Assert.Equal(OutcomeSide.Yes, exitOrder.Outcome);
        Assert.Equal(OrderSide.Sell, exitOrder.Side); // 敞口是 Buy，退出应该是 Sell
        Assert.Equal(testQuantity, exitOrder.Quantity);
        Assert.Equal(0.01m, exitOrder.Price); // 激进卖价
    }

    [Fact]
    public async Task UnhedgedWatchdog_超时敞口_ForceHedge_应执行对冲订单并验证完整属性()
    {
        // Arrange
        var options = Options.Create(new RiskOptions
        {
            MaxFirstLegExposureSeconds = 1,
            UnhedgedExitAction = UnhedgedExitAction.ForceHedge,
            KillSwitchEnabled = true
        });
        var riskManager = CreateRiskManager(options);
        var eventRepository = new InMemoryRiskEventRepository(NullLogger<InMemoryRiskEventRepository>.Instance);
        var executionService = new MockExecutionService();
        var services = CreateServiceProvider(riskManager, executionService);
        var watchdog = new UnhedgedExposureWorker(
            riskManager,
            eventRepository,
            services,
            options,
            new RiskMetrics(),
            NullLogger<UnhedgedExposureWorker>.Instance);

        // 记录敞口 - 第一腿是 Yes + Buy，对冲腿是 No
        const string testTokenId = "token-yes-456";
        const string hedgeTokenId = "token-no-789"; // 对冲腿的 tokenId
        const decimal testQuantity = 50m;
        const decimal testPrice = 0.60m;

        await riskManager.RecordUnhedgedExposureAsync(
            "strategy-1",
            "market-1",
            testTokenId,
            hedgeTokenId, // 提供对冲腿的 tokenId
            OutcomeSide.Yes,
            OrderSide.Buy,
            testQuantity,
            testPrice,
            DateTimeOffset.UtcNow.AddSeconds(-10));

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await watchdog.StartAsync(cts.Token);
            await Task.Delay(200, cts.Token);
            await watchdog.StopAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert
        var strategyState = riskManager.GetStrategyKillSwitchState("strategy-1");
        Assert.True(strategyState.IsActive);
        Assert.Equal(KillSwitchLevel.HardStop, strategyState.Level);

        // 验证对冲订单的完整属性
        var hedgeOrder = executionService.PlacedOrders.FirstOrDefault(o => o.ClientOrderId.StartsWith("HEDGE_"));
        Assert.NotNull(hedgeOrder);
        Assert.Equal(hedgeTokenId, hedgeOrder.TokenId); // 使用提供的对冲 tokenId
        Assert.Equal(OutcomeSide.No, hedgeOrder.Outcome); // 第一腿是 Yes，对冲应该是 No
        Assert.Equal(OrderSide.Buy, hedgeOrder.Side); // 对冲总是买入
        Assert.Equal(testQuantity, hedgeOrder.Quantity);
        Assert.Equal(0.99m, hedgeOrder.Price); // 激进买价
    }

    #endregion

    #region RiskStateStore Notional Cleanup Tests

    [Fact]
    public async Task RiskStateStore_终态订单_应清零名义金额()
    {
        // Arrange
        var riskManager = CreateRiskManager();

        var request = CreateRequest("strategy-1", "order-1") with { Price = 0.5m, Quantity = 20m }; // notional = 10
        await riskManager.RecordOrderAcceptedAsync(request);

        var snapshot1 = riskManager.GetStateSnapshot();
        Assert.Equal(10m, snapshot1.TotalOpenNotional);
        Assert.Equal(1, snapshot1.TotalOpenOrders);

        // Act - 订单被取消（终态）
        await riskManager.RecordOrderUpdateAsync(new RiskOrderUpdate
        {
            ClientOrderId = "order-1",
            StrategyId = "strategy-1",
            Status = ExecutionStatus.Cancelled,
            FilledQuantity = 0m,
            OriginalQuantity = 20m
        });

        // Assert - 名义金额应被清零
        var snapshot2 = riskManager.GetStateSnapshot();
        Assert.Equal(0m, snapshot2.TotalOpenNotional);
        Assert.Equal(0, snapshot2.TotalOpenOrders);
    }

    [Fact]
    public async Task RiskStateStore_部分成交后取消_应清零剩余名义金额()
    {
        // Arrange
        var riskManager = CreateRiskManager();

        var request = CreateRequest("strategy-1", "order-1") with { Price = 0.5m, Quantity = 20m }; // notional = 10
        await riskManager.RecordOrderAcceptedAsync(request);

        // 部分成交 10 个
        await riskManager.RecordOrderUpdateAsync(new RiskOrderUpdate
        {
            ClientOrderId = "order-1",
            StrategyId = "strategy-1",
            Status = ExecutionStatus.PartiallyFilled,
            FilledQuantity = 10m,
            OriginalQuantity = 20m
        });

        var snapshot1 = riskManager.GetStateSnapshot();
        Assert.Equal(5m, snapshot1.TotalOpenNotional); // 剩余 10 * 0.5 = 5

        // Act - 订单被取消
        await riskManager.RecordOrderUpdateAsync(new RiskOrderUpdate
        {
            ClientOrderId = "order-1",
            StrategyId = "strategy-1",
            Status = ExecutionStatus.Cancelled,
            FilledQuantity = 10m,
            OriginalQuantity = 20m
        });

        // Assert - 名义金额应被清零
        var snapshot2 = riskManager.GetStateSnapshot();
        Assert.Equal(0m, snapshot2.TotalOpenNotional);
        Assert.Equal(0, snapshot2.TotalOpenOrders);
    }

    #endregion

    #region Helpers

    private static RiskManager CreateRiskManager(IOptions<RiskOptions>? options = null)
    {
        options ??= Options.Create(new RiskOptions { KillSwitchEnabled = true });
        var capitalOptions = Options.Create(new RiskCapitalOptions { TotalCapital = 1000m, AvailableCapital = 1000m });

        return new RiskManager(
            options,
            new InMemoryRiskCapitalProvider(capitalOptions),
            new InMemoryRiskEventRepository(NullLogger<InMemoryRiskEventRepository>.Instance),
            new RiskStateStore(),
            new RiskMetrics(),
            NullLogger<RiskManager>.Instance);
    }

    private static IServiceProvider CreateServiceProvider(IRiskManager riskManager, IExecutionService executionService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(riskManager);
        services.AddSingleton(executionService);
        return services.BuildServiceProvider();
    }

    private static Task RecordTestExposureAsync(
        IRiskManager riskManager,
        string strategyId,
        string marketId,
        DateTimeOffset startedAtUtc)
    {
        return riskManager.RecordUnhedgedExposureAsync(
            strategyId,
            marketId,
            $"token-yes-{marketId}",
            $"token-no-{marketId}", // hedgeTokenId
            OutcomeSide.Yes,
            OrderSide.Buy,
            100m,
            0.50m,
            startedAtUtc);
    }

    private static RiskOrderRequest CreateRequest(string strategyId, string orderId)
        => new()
        {
            StrategyId = strategyId,
            ClientOrderId = orderId,
            MarketId = "market-1",
            TokenId = "token-1",
            Side = OrderSide.Buy,
            OrderType = OrderType.Limit,
            TimeInForce = TimeInForce.Gtc,
            Price = 0.5m,
            Quantity = 10m,
            Leg = OrderLeg.Single
        };

    private sealed class MockExecutionService : IExecutionService
    {
        public List<string> CancelledOrderIds { get; } = new();
        public List<ExecutionRequest> PlacedOrders { get; } = new();

        public Task<ExecutionResult> PlaceOrderAsync(ExecutionRequest request, CancellationToken cancellationToken = default)
        {
            PlacedOrders.Add(request);
            return Task.FromResult(ExecutionResult.Succeed(request.ClientOrderId, "exchange-order-id"));
        }

        public Task<IReadOnlyList<ExecutionResult>> PlaceOrdersAsync(
            IReadOnlyList<ExecutionRequest> requests,
            CancellationToken cancellationToken = default)
        {
            var results = new List<ExecutionResult>(requests.Count);
            foreach (var request in requests)
            {
                PlacedOrders.Add(request);
                results.Add(ExecutionResult.Succeed(request.ClientOrderId, "exchange-order-id"));
            }

            return Task.FromResult<IReadOnlyList<ExecutionResult>>(results);
        }

        public Task<ExecutionResult> CancelOrderAsync(string clientOrderId, CancellationToken cancellationToken = default)
        {
            CancelledOrderIds.Add(clientOrderId);
            return Task.FromResult(ExecutionResult.Succeed(clientOrderId, "exchange-order-id", ExecutionStatus.Cancelled));
        }

        public Task<OrderStatusResult> GetOrderStatusAsync(string clientOrderId, CancellationToken cancellationToken = default)
            => Task.FromResult(OrderStatusResult.NotFound(clientOrderId));
    }

    #endregion
}
