// ============================================================================
// 策略管理器
// ============================================================================
// 策略引擎的核心管理组件（后台服务），负责：
// - 策略生命周期管理（启动/暂停/恢复/停止）
// - 策略市场数据分发（基于 Channel 的隔离架构）
// - 策略订单状态轮询和回调路由
// - 运行时状态持久化
// ============================================================================

using System.Collections.Concurrent;
using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.OrderBook;
using Autotrade.MarketData.Application.Contract.Snapshots;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Decisions;
using Autotrade.Strategy.Application.Engine;
using Autotrade.Strategy.Application.Observations;
using Autotrade.Strategy.Application.Orders;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Strategy.Infra.BackgroundJobs.Workers;

/// <summary>
/// 策略管理器。
/// 管理所有策略的生命周期和市场数据分发。
/// </summary>
public sealed class StrategyManagerWorker : BackgroundService, IStrategyManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IStrategyFactory _factory;
    private readonly IMarketSnapshotProvider _snapshotProvider;
    private readonly StrategyOrderRegistry _orderRegistry;
    private readonly IOptionsMonitor<StrategyEngineOptions> _options;
    private readonly ILogger<StrategyManagerWorker> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly StrategyRuntimeStore _runtimeStore;
    private readonly IStrategyRunStateRepository _stateRepository;

    // 该集合会被多个后台任务（control loop / poller / CLI 调用）并发读取，因此使用并发字典。
    private readonly ConcurrentDictionary<string, StrategySupervisor> _supervisors = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, StrategyState> _desiredStateOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly StrategyDataRouter _dataRouter;
    private readonly StrategyOrderUpdateRouter _orderUpdateRouter;
    private Task? _dataRouterTask;
    private Task? _controlLoopTask;

    public StrategyManagerWorker(
        IServiceScopeFactory scopeFactory,
        IStrategyFactory factory,
        IMarketSnapshotProvider snapshotProvider,
        IOrderBookSubscriptionService orderBookSubscriptionService,
        StrategyOrderRegistry orderRegistry,
        IOptionsMonitor<StrategyEngineOptions> options,
        ILogger<StrategyManagerWorker> logger,
        ILoggerFactory loggerFactory,
        StrategyRuntimeStore runtimeStore,
        IStrategyRunStateRepository stateRepository)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
        _orderRegistry = orderRegistry ?? throw new ArgumentNullException(nameof(orderRegistry));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _runtimeStore = runtimeStore ?? throw new ArgumentNullException(nameof(runtimeStore));
        _stateRepository = stateRepository ?? throw new ArgumentNullException(nameof(stateRepository));

        _dataRouter = new StrategyDataRouter(
            snapshotProvider,
            loggerFactory.CreateLogger<StrategyDataRouter>(),
            orderBookSubscriptionService);

        _orderUpdateRouter = new StrategyOrderUpdateRouter(
            loggerFactory.CreateLogger<StrategyOrderUpdateRouter>());
    }

    public IReadOnlyList<StrategyDescriptor> GetRegisteredStrategies()
        => _factory.GetDescriptors();

    public Task<IReadOnlyList<StrategyStatus>> GetStatusesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_runtimeStore.GetAllStatuses());
    }

    public StrategyState GetDesiredState(string strategyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);

        var descriptor = _factory
            .GetDescriptors()
            .FirstOrDefault(d => string.Equals(d.StrategyId, strategyId, StringComparison.OrdinalIgnoreCase));

        return descriptor is null
            ? StrategyState.Stopped
            : ResolveDesiredState(descriptor, _options.CurrentValue);
    }

    public async Task SetDesiredStateAsync(
        string strategyId,
        StrategyState desiredState,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);
        ValidateDesiredState(desiredState);

        var descriptor = _factory
            .GetDescriptors()
            .FirstOrDefault(d => string.Equals(d.StrategyId, strategyId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Unknown strategy id: {strategyId}.");

        if (!descriptor.Enabled && desiredState != StrategyState.Stopped)
        {
            throw new InvalidOperationException($"Strategy {strategyId} is disabled and cannot be set to {desiredState}.");
        }

        _desiredStateOverrides[descriptor.StrategyId] = desiredState;
        await PersistDesiredStateAsync(descriptor, desiredState, cancellationToken).ConfigureAwait(false);
        await ApplyTargetStateAsync(descriptor.StrategyId, desiredState, cancellationToken).ConfigureAwait(false);
    }

    public async Task StartAsync(string strategyId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(strategyId))
        {
            throw new ArgumentException("StrategyId cannot be empty.", nameof(strategyId));
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_supervisors.TryGetValue(strategyId, out var existing))
            {
                await existing.StartAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (_supervisors.Count >= _options.CurrentValue.MaxConcurrentStrategies)
            {
                throw new InvalidOperationException("MaxConcurrentStrategies limit reached.");
            }

            var supervisor = CreateSupervisor(strategyId);
            _supervisors[strategyId] = supervisor;

            await supervisor.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task PauseAsync(string strategyId, CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_supervisors.TryGetValue(strategyId, out var supervisor))
            {
                await supervisor.PauseAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task ResumeAsync(string strategyId, CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_supervisors.TryGetValue(strategyId, out var supervisor))
            {
                await supervisor.ResumeAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(string strategyId, CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_supervisors.TryGetValue(strategyId, out var supervisor))
            {
                await supervisor.StopAsync(cancellationToken).ConfigureAwait(false);
                _supervisors.TryRemove(strategyId, out _);
                _dataRouter.UnregisterStrategy(strategyId);
                _orderUpdateRouter.UnregisterStrategy(strategyId);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task ReloadConfigAsync(string strategyId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(strategyId))
        {
            throw new ArgumentException("StrategyId cannot be empty.", nameof(strategyId));
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogInformation("Reloading config for strategy {StrategyId}", strategyId);

            // Step 1: Pause the running strategy (if exists)
            if (_supervisors.TryGetValue(strategyId, out var oldSupervisor))
            {
                await oldSupervisor.PauseAsync(cancellationToken).ConfigureAwait(false);
                await oldSupervisor.StopAsync(cancellationToken).ConfigureAwait(false);
                _supervisors.TryRemove(strategyId, out _);
                _dataRouter.UnregisterStrategy(strategyId);
                _orderUpdateRouter.UnregisterStrategy(strategyId);
            }

            // Step 2: Refresh descriptor from factory (re-reads config)
            var descriptor = _factory
                .GetDescriptors()
                .FirstOrDefault(d => string.Equals(d.StrategyId, strategyId, StringComparison.OrdinalIgnoreCase));

            if (descriptor is null)
            {
                _logger.LogWarning("Strategy {StrategyId} not found in factory after reload", strategyId);
                return;
            }

            // Step 3: Check if strategy is enabled before restarting
            if (!descriptor.Enabled)
            {
                _logger.LogInformation("Strategy {StrategyId} is disabled in config, not restarting after reload", strategyId);
                return;
            }

            // Step 4: Create new supervisor with new config version
            var newSupervisor = CreateSupervisor(strategyId);
            _supervisors[strategyId] = newSupervisor;

            // Step 5: Start the new instance
            await newSupervisor.StartAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Strategy {StrategyId} reloaded with config version {Version}", strategyId, descriptor.ConfigVersion);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var descriptors = _factory.GetDescriptors();
        _runtimeStore.Initialize(descriptors);
        await LoadDesiredStateOverridesAsync(stoppingToken).ConfigureAwait(false);

        foreach (var descriptor in descriptors)
        {
            var desiredState = ResolveDesiredState(descriptor, _options.CurrentValue);
            var initialState = desiredState == StrategyState.Stopped ? StrategyState.Stopped : StrategyState.Created;
            var status = new StrategyStatus(
                descriptor.StrategyId,
                descriptor.Name,
                initialState,
                descriptor.Enabled,
                descriptor.ConfigVersion,
                0,
                null,
                null,
                null,
                null,
                DesiredState: desiredState);
            _runtimeStore.UpdateStatus(status);
            await PersistStatusAsync(status).ConfigureAwait(false);
        }

        // Start the data router for channel-based distribution
        var routerInterval = TimeSpan.FromSeconds(_options.CurrentValue.EvaluationIntervalSeconds / 2.0);
        _dataRouterTask = Task.Run(() => _dataRouter.RunAsync(routerInterval, stoppingToken), stoppingToken);

        if (_options.CurrentValue.Enabled)
        {
            var enabledCount = descriptors.Count(d => d.Enabled);
            _logger.LogInformation(
                "策略引擎启动: 注册 {Total} 个策略，启用 {Enabled} 个，MaxConcurrent={Max}",
                descriptors.Count,
                enabledCount,
                _options.CurrentValue.MaxConcurrentStrategies);
        }
        else
        {
            _logger.LogWarning("策略引擎已禁用 (StrategyEngine:Enabled = false)");
        }

        // Control loop：对齐 DesiredStates / Enabled 与实际 supervisor 状态（跨进程控制面）
        // 由 control loop 统一决定“哪些策略启动/暂停/停止”，避免与启动阶段逻辑重复。
        _controlLoopTask = Task.Run(() => RunControlLoopAsync(stoppingToken), stoppingToken);

        using var pollerScope = _scopeFactory.CreateScope();
        var poller = CreateOrderStatusPoller(pollerScope);
        await poller.RunAsync(stoppingToken).ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _dataRouter.Dispose();
        _orderUpdateRouter.Dispose();

        if (_dataRouterTask is not null)
        {
            await Task.WhenAny(_dataRouterTask, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken))
                .ConfigureAwait(false);
        }

        if (_controlLoopTask is not null)
        {
            await Task.WhenAny(_controlLoopTask, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken))
                .ConfigureAwait(false);
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private StrategyOrderStatusPoller CreateOrderStatusPoller(IServiceScope scope)
    {
        var executionService = scope.ServiceProvider.GetRequiredService<IExecutionService>();
        var riskManager = scope.ServiceProvider.GetRequiredService<IRiskManager>();
        var logger = _loggerFactory.CreateLogger<StrategyOrderStatusPoller>();

        return new StrategyOrderStatusPoller(
            executionService,
            riskManager,
            _orderRegistry,
            Microsoft.Extensions.Options.Options.Create(_options.CurrentValue),
            logger,
            strategyId =>
            {
                return _supervisors.TryGetValue(strategyId, out var supervisor)
                    ? supervisor.Strategy
                    : null;
            },
            _orderUpdateRouter);
    }

    private async Task RunControlLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileDesiredStatesAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Strategy control loop failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ReconcileDesiredStatesAsync(CancellationToken cancellationToken)
    {
        var opt = _options.CurrentValue;

        // If engine is disabled, ensure everything is stopped.
        if (!opt.Enabled)
        {
            foreach (var id in _supervisors.Keys.ToList())
            {
                await StopAsync(id, cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        var descriptors = _factory.GetDescriptors();

        // Build allowed set respecting MaxConcurrentStrategies; prioritize explicit DesiredStates entries.
        var max = Math.Max(0, opt.MaxConcurrentStrategies);
        if (max == 0)
        {
            foreach (var id in _supervisors.Keys.ToList())
            {
                await StopAsync(id, cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        var enabled = descriptors.Where(d => d.Enabled).ToList();
        var desiredByStrategy = descriptors.ToDictionary(
            d => d.StrategyId,
            d => ResolveDesiredState(d, opt),
            StringComparer.OrdinalIgnoreCase);

        var explicitWanted = enabled
            .Where(d => IsExplicitDesiredState(d.StrategyId, opt) && desiredByStrategy[d.StrategyId] != StrategyState.Stopped)
            .OrderBy(d => d.StrategyId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var implicitWanted = enabled
            .Where(d => !IsExplicitDesiredState(d.StrategyId, opt) && desiredByStrategy[d.StrategyId] != StrategyState.Stopped)
            .OrderBy(d => d.StrategyId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in explicitWanted)
        {
            if (allowed.Count >= max) break;
            allowed.Add(d.StrategyId);
        }

        foreach (var d in implicitWanted)
        {
            if (allowed.Count >= max) break;
            allowed.Add(d.StrategyId);
        }

        // Apply targets
        foreach (var d in descriptors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var target = StrategyState.Stopped;
            if (d.Enabled && allowed.Contains(d.StrategyId))
            {
                target = desiredByStrategy[d.StrategyId];
            }

            await ApplyTargetStateAsync(d.StrategyId, target, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ApplyTargetStateAsync(
        string strategyId,
        StrategyState target,
        CancellationToken cancellationToken)
    {
        ValidateDesiredState(target);

        if (!_supervisors.TryGetValue(strategyId, out var supervisor))
        {
            if (target == StrategyState.Running)
            {
                await StartAsync(strategyId, cancellationToken).ConfigureAwait(false);
            }
            else if (target == StrategyState.Paused)
            {
                await StartAsync(strategyId, cancellationToken).ConfigureAwait(false);
                await PauseAsync(strategyId, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await MarkStoppedWithoutSupervisorAsync(strategyId, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        if (target == StrategyState.Stopped)
        {
            await StopAsync(strategyId, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (target == StrategyState.Paused && supervisor.State == StrategyState.Running)
        {
            await PauseAsync(strategyId, cancellationToken).ConfigureAwait(false);
        }
        else if (target == StrategyState.Running && supervisor.State == StrategyState.Paused)
        {
            await ResumeAsync(strategyId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task MarkStoppedWithoutSupervisorAsync(string strategyId, CancellationToken cancellationToken)
    {
        var descriptor = _factory
            .GetDescriptors()
            .FirstOrDefault(d => string.Equals(d.StrategyId, strategyId, StringComparison.OrdinalIgnoreCase));

        if (descriptor is null)
        {
            return;
        }

        var desiredState = ResolveDesiredState(descriptor, _options.CurrentValue);
        var current = _runtimeStore.GetStatus(strategyId);
        var status = current is null
            ? new StrategyStatus(
                descriptor.StrategyId,
                descriptor.Name,
                StrategyState.Stopped,
                descriptor.Enabled,
                descriptor.ConfigVersion,
                0,
                null,
                null,
                null,
                null,
                DesiredState: desiredState)
            : current with
            {
                State = StrategyState.Stopped,
                DesiredState = desiredState
            };

        _runtimeStore.UpdateStatus(status);
        await PersistStatusAsync(status, cancellationToken).ConfigureAwait(false);
    }

    private async Task LoadDesiredStateOverridesAsync(CancellationToken cancellationToken)
    {
        var statuses = await _stateRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        foreach (var status in statuses)
        {
            if (status.DesiredState is { } desiredState)
            {
                _desiredStateOverrides[status.StrategyId] = desiredState;
            }
        }
    }

    private StrategyState ResolveDesiredState(StrategyDescriptor descriptor, StrategyEngineOptions options)
    {
        if (_desiredStateOverrides.TryGetValue(descriptor.StrategyId, out var runtimeDesired))
        {
            return runtimeDesired;
        }

        if (options.DesiredStates.TryGetValue(descriptor.StrategyId, out var configuredDesired))
        {
            return configuredDesired;
        }

        return options.Enabled && descriptor.Enabled ? StrategyState.Running : StrategyState.Stopped;
    }

    private bool IsExplicitDesiredState(string strategyId, StrategyEngineOptions options)
    {
        return _desiredStateOverrides.ContainsKey(strategyId) || options.DesiredStates.ContainsKey(strategyId);
    }

    private async Task PersistDesiredStateAsync(
        StrategyDescriptor descriptor,
        StrategyState desiredState,
        CancellationToken cancellationToken)
    {
        var current = _runtimeStore.GetStatus(descriptor.StrategyId);
        var status = current is null
            ? new StrategyStatus(
                descriptor.StrategyId,
                descriptor.Name,
                StrategyState.Created,
                descriptor.Enabled,
                descriptor.ConfigVersion,
                0,
                null,
                null,
                null,
                null,
                DesiredState: desiredState)
            : current with { DesiredState = desiredState };

        _runtimeStore.UpdateStatus(status);
        await PersistStatusAsync(status, cancellationToken).ConfigureAwait(false);
        await _stateRepository.SetDesiredStateAsync(descriptor.StrategyId, desiredState, cancellationToken)
            .ConfigureAwait(false);
    }

    private static void ValidateDesiredState(StrategyState desiredState)
    {
        if (desiredState is StrategyState.Created or StrategyState.Faulted)
        {
            throw new ArgumentOutOfRangeException(
                nameof(desiredState),
                desiredState,
                "Desired state must be Running, Paused, or Stopped.");
        }
    }

    private StrategySupervisor CreateSupervisor(string strategyId)
    {
        var descriptor = _factory
            .GetDescriptors()
            .First(d => string.Equals(d.StrategyId, strategyId, StringComparison.OrdinalIgnoreCase));

        var scope = _scopeFactory.CreateScope();
        var serviceProvider = scope.ServiceProvider;

        var context = new StrategyContext
        {
            StrategyId = strategyId,
            ExecutionService = serviceProvider.GetRequiredService<IExecutionService>(),
            OrderBookReader = serviceProvider.GetRequiredService<IOrderBookReader>(),
            MarketCatalog = serviceProvider.GetRequiredService<IMarketCatalogReader>(),
            MarketDataSnapshotReader = serviceProvider.GetService<IMarketDataSnapshotReader>(),
            RiskManager = serviceProvider.GetRequiredService<IRiskManager>(),
            DecisionLogger = serviceProvider.GetRequiredService<IStrategyDecisionLogger>(),
            ObservationLogger = serviceProvider.GetRequiredService<IStrategyObservationLogger>()
        };

        var strategy = _factory.CreateStrategy(strategyId, context);
        var control = new StrategyControl();

        // Register channels for this strategy (provides backpressure/isolation)
        var snapshotChannel = _dataRouter.RegisterStrategy(strategyId, channelCapacity: 100);
        var orderUpdateChannel = _orderUpdateRouter.RegisterStrategy(strategyId, channelCapacity: 100);

        StrategySupervisor? supervisor = null;
        var runner = new StrategyRunner(
            strategy,
            context,
            _snapshotProvider,
            _orderRegistry,
            control,
            Microsoft.Extensions.Options.Options.Create(_options.CurrentValue),
            _loggerFactory.CreateLogger<StrategyRunner>(),
            timestamp => supervisor?.NotifyHeartbeatAsync(timestamp) ?? Task.CompletedTask,
            timestamp => supervisor?.NotifyDecisionAsync(timestamp) ?? Task.CompletedTask,
            (markets, cycles, snapshots) => supervisor?.NotifyStatsAsync(markets, cycles, snapshots) ?? Task.CompletedTask,
            snapshotChannel,
            _dataRouter,
            orderUpdateChannel);

        supervisor = new StrategySupervisor(
            descriptor,
            strategy,
            runner,
            control,
            Microsoft.Extensions.Options.Options.Create(_options.CurrentValue),
            _loggerFactory.CreateLogger<StrategySupervisor>(),
            UpdateStatusAsync,
            scope,
            snapshotChannel,
            () => context.RiskManager.IsKillSwitchActive || context.RiskManager.IsStrategyBlocked(strategyId));

        return supervisor;
    }

    private async Task UpdateStatusAsync(StrategyStatus status)
    {
        var descriptor = _factory
            .GetDescriptors()
            .FirstOrDefault(d => string.Equals(d.StrategyId, status.StrategyId, StringComparison.OrdinalIgnoreCase));
        var desiredState = descriptor is null
            ? status.DesiredState
            : ResolveDesiredState(descriptor, _options.CurrentValue);
        var enriched = status with { DesiredState = desiredState };

        _runtimeStore.UpdateStatus(enriched);
        await PersistStatusAsync(enriched).ConfigureAwait(false);
    }

    private Task PersistStatusAsync(StrategyStatus status, CancellationToken cancellationToken = default)
    {
        return _stateRepository.UpsertAsync(status, cancellationToken);
    }
}
