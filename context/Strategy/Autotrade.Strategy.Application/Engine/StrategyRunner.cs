// ============================================================================
// 策略执行器
// ============================================================================
// 策略的实际执行循环，负责：
// - 周期性评估入场/出场信号
// - 风险校验和订单提交
// - 订单状态回调处理
// - 运行时统计收集
// 
// 支持两种模式：
// - Channel 模式：从独立的 Channel 读取市场快照（推荐）
// - Polling 模式：直接轮询快照提供者（兼容旧代码）
// ============================================================================

using System.Diagnostics;
using System.Text.Json;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Decisions;
using Autotrade.Strategy.Application.Observability;
using Autotrade.Strategy.Application.Orders;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Strategy.Application.Engine;

/// <summary>
/// 策略执行器。
/// 执行策略的评估循环，处理信号和订单。
/// </summary>
public sealed class StrategyRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITradingStrategy _strategy;
    private readonly StrategyContext _context;
    private readonly IMarketSnapshotProvider _snapshotProvider;
    private readonly StrategyOrderRegistry _orderRegistry;
    private readonly StrategyEngineOptions _options;
    private readonly StrategyControl _control;
    private readonly ILogger<StrategyRunner> _logger;
    private readonly Func<DateTimeOffset, Task>? _onHeartbeat;
    private readonly Func<DateTimeOffset, Task>? _onDecision;
    private readonly Func<IReadOnlyList<string>, long, long, Task>? _onStats;
    private readonly StrategyMarketChannel? _channel;
    private readonly StrategyDataRouter? _router;
    private readonly StrategyOrderUpdateChannel? _orderUpdateChannel;

    // Runtime statistics
    private long _cycleCount;
    private long _snapshotsProcessed;
    private readonly HashSet<string> _activeMarkets = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _statsLock = new();

    public StrategyRunner(
        ITradingStrategy strategy,
        StrategyContext context,
        IMarketSnapshotProvider snapshotProvider,
        StrategyOrderRegistry orderRegistry,
        StrategyControl control,
        IOptions<StrategyEngineOptions> options,
        ILogger<StrategyRunner> logger,
        Func<DateTimeOffset, Task>? onHeartbeat = null,
        Func<DateTimeOffset, Task>? onDecision = null,
        Func<IReadOnlyList<string>, long, long, Task>? onStats = null,
        StrategyMarketChannel? channel = null,
        StrategyDataRouter? router = null,
        StrategyOrderUpdateChannel? orderUpdateChannel = null)
    {
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
        _orderRegistry = orderRegistry ?? throw new ArgumentNullException(nameof(orderRegistry));
        _control = control ?? throw new ArgumentNullException(nameof(control));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _onHeartbeat = onHeartbeat;
        _onDecision = onDecision;
        _onStats = onStats;
        _channel = channel;
        _router = router;
        _orderUpdateChannel = orderUpdateChannel;
    }

    public long CycleCount => Interlocked.Read(ref _cycleCount);
    public long SnapshotsProcessed => Interlocked.Read(ref _snapshotsProcessed);

    public IReadOnlyList<string> GetActiveMarkets()
    {
        lock (_statsLock)
        {
            return _activeMarkets.ToList();
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _options.Validate();

        await _strategy.StartAsync(cancellationToken).ConfigureAwait(false);

        // If we have a channel and router, use channel-based mode; otherwise fall back to polling
        if (_channel is not null && _router is not null)
        {
            await RunWithChannelAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await RunWithPollingAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Channel-based mode: reads from per-strategy bounded channel (provides backpressure/isolation).
    /// </summary>
    private async Task RunWithChannelAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(_options.EvaluationIntervalSeconds);

        while (!cancellationToken.IsCancellationRequested)
        {
            await _control.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
            var cycleSw = Stopwatch.StartNew();

            if (_context.RiskManager.IsKillSwitchActive || _context.RiskManager.IsStrategyBlocked(_strategy.Id))
            {
                _logger.LogWarning(
                    "Kill switch active, strategy {StrategyId} paused. Global={GlobalActive}, StrategyBlocked={StrategyBlocked}",
                    _strategy.Id,
                    _context.RiskManager.IsKillSwitchActive,
                    _context.RiskManager.IsStrategyBlocked(_strategy.Id));
                await LogObservationAsync(
                    "Cycle",
                    "Rejected",
                    "kill_switch_active",
                    null,
                    JsonSerializer.Serialize(new
                    {
                        globalKillSwitch = _context.RiskManager.IsKillSwitchActive,
                        strategyBlocked = _context.RiskManager.IsStrategyBlocked(_strategy.Id)
                    }, JsonOptions),
                    null,
                    null,
                    cancellationToken).ConfigureAwait(false);
                await ProcessPendingOrderUpdatesAsync(cancellationToken).ConfigureAwait(false);
                await NotifyStatsAsync().ConfigureAwait(false);
                cycleSw.Stop();
                StrategyMetrics.RecordCycleDuration(_strategy.Id, cycleSw.Elapsed.TotalMilliseconds);
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                continue;
            }

            Interlocked.Increment(ref _cycleCount);

            if (_onHeartbeat is not null)
            {
                await _onHeartbeat(DateTimeOffset.UtcNow).ConfigureAwait(false);
            }

            // Update subscriptions based on strategy's market selection
            var marketIds = await _strategy.SelectMarketsAsync(cancellationToken).ConfigureAwait(false);
            var marketIdList = marketIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
            await LogMarketSelectionAsync(marketIdList, cancellationToken).ConfigureAwait(false);

            UpdateActiveMarkets(marketIdList);
            _router!.UpdateSubscriptions(_strategy.Id, marketIdList);

            IReadOnlyList<MarketSnapshot> snapshots = Array.Empty<MarketSnapshot>();
            if (marketIdList.Count > 0)
            {
                // Read batch from channel with timeout; on timeout just continue (don't exit loop)
                try
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.SnapshotTimeoutSeconds));

                    var maxSnapshotBatch = Math.Max(marketIdList.Count, _options.MaxOrdersPerCycle * 2);
                    snapshots = await _channel!.ReadBatchAsync(maxSnapshotBatch, timeoutCts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Snapshot timeout, not shutdown - continue loop
                    _logger.LogDebug("Snapshot read timeout for strategy {StrategyId}, continuing", _strategy.Id);
                    await LogObservationAsync(
                        "Snapshot",
                        "Timeout",
                        "snapshot_channel_timeout",
                        null,
                        JsonSerializer.Serialize(new { marketCount = marketIdList.Count }, JsonOptions),
                        null,
                        null,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            if (snapshots.Count > 0)
            {
                var snapshotSw = Stopwatch.StartNew();
                await ProcessSnapshotsAsync(snapshots, cancellationToken).ConfigureAwait(false);
                snapshotSw.Stop();
                StrategyMetrics.RecordSnapshotProcessingDuration(_strategy.Id, snapshotSw.Elapsed.TotalMilliseconds);
            }

            // Process pending order updates from channel
            await ProcessPendingOrderUpdatesAsync(cancellationToken).ConfigureAwait(false);

            await NotifyStatsAsync().ConfigureAwait(false);

            cycleSw.Stop();
            StrategyMetrics.RecordCycleDuration(_strategy.Id, cycleSw.Elapsed.TotalMilliseconds);
            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Processes pending order updates from the channel (non-blocking).
    /// </summary>
    private async Task ProcessPendingOrderUpdatesAsync(CancellationToken cancellationToken)
    {
        if (_orderUpdateChannel is null)
        {
            return;
        }

        var updates = _orderUpdateChannel.TryReadAll();
        foreach (var update in updates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _strategy.OnOrderUpdateAsync(update, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing order update {ClientOrderId} for strategy {StrategyId}",
                    update.ClientOrderId, _strategy.Id);
            }
        }
    }

    /// <summary>
    /// Polling mode: directly fetches snapshots (original behavior, for backwards compatibility).
    /// </summary>
    private async Task RunWithPollingAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(_options.EvaluationIntervalSeconds);

        while (!cancellationToken.IsCancellationRequested)
        {
            await _control.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
            var cycleSw = Stopwatch.StartNew();

            if (_context.RiskManager.IsKillSwitchActive || _context.RiskManager.IsStrategyBlocked(_strategy.Id))
            {
                _logger.LogWarning(
                    "Kill switch active, strategy {StrategyId} paused. Global={GlobalActive}, StrategyBlocked={StrategyBlocked}",
                    _strategy.Id,
                    _context.RiskManager.IsKillSwitchActive,
                    _context.RiskManager.IsStrategyBlocked(_strategy.Id));
                await LogObservationAsync(
                    "Cycle",
                    "Rejected",
                    "kill_switch_active",
                    null,
                    JsonSerializer.Serialize(new
                    {
                        globalKillSwitch = _context.RiskManager.IsKillSwitchActive,
                        strategyBlocked = _context.RiskManager.IsStrategyBlocked(_strategy.Id)
                    }, JsonOptions),
                    null,
                    null,
                    cancellationToken).ConfigureAwait(false);
                await ProcessPendingOrderUpdatesAsync(cancellationToken).ConfigureAwait(false);
                await NotifyStatsAsync().ConfigureAwait(false);
                cycleSw.Stop();
                StrategyMetrics.RecordCycleDuration(_strategy.Id, cycleSw.Elapsed.TotalMilliseconds);
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                continue;
            }

            Interlocked.Increment(ref _cycleCount);

            if (_onHeartbeat is not null)
            {
                await _onHeartbeat(DateTimeOffset.UtcNow).ConfigureAwait(false);
            }

            var marketIds = await _strategy.SelectMarketsAsync(cancellationToken).ConfigureAwait(false);
            var marketIdList = marketIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
            await LogMarketSelectionAsync(marketIdList, cancellationToken).ConfigureAwait(false);

            UpdateActiveMarkets(marketIdList);

            IReadOnlyList<MarketSnapshot> snapshots = Array.Empty<MarketSnapshot>();
            if (marketIdList.Count > 0)
            {
                try
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.SnapshotTimeoutSeconds));

                    snapshots = await _snapshotProvider
                        .GetSnapshotsAsync(marketIdList, timeoutCts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Snapshot fetch timeout for strategy {StrategyId}, continuing", _strategy.Id);
                    await LogObservationAsync(
                        "Snapshot",
                        "Timeout",
                        "snapshot_fetch_timeout",
                        null,
                        JsonSerializer.Serialize(new { marketCount = marketIdList.Count }, JsonOptions),
                        null,
                        null,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            if (snapshots.Count > 0)
            {
                var snapshotSw = Stopwatch.StartNew();
                await ProcessSnapshotsAsync(snapshots, cancellationToken).ConfigureAwait(false);
                snapshotSw.Stop();
                StrategyMetrics.RecordSnapshotProcessingDuration(_strategy.Id, snapshotSw.Elapsed.TotalMilliseconds);
            }

            await ProcessPendingOrderUpdatesAsync(cancellationToken).ConfigureAwait(false);

            await NotifyStatsAsync().ConfigureAwait(false);

            cycleSw.Stop();
            StrategyMetrics.RecordCycleDuration(_strategy.Id, cycleSw.Elapsed.TotalMilliseconds);
            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessSnapshotsAsync(
        IReadOnlyList<MarketSnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        var ordersSubmitted = 0;

        foreach (var snapshot in snapshots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Interlocked.Increment(ref _snapshotsProcessed);

            var entrySignal = await _strategy
                .EvaluateEntryAsync(snapshot, cancellationToken)
                .ConfigureAwait(false);

            await LogSignalObservationAsync("Entry", snapshot, entrySignal, cancellationToken)
                .ConfigureAwait(false);

            if (entrySignal is not null)
            {
                StrategyMetrics.RecordDecisionLatency(
                    _strategy.Id,
                    (DateTimeOffset.UtcNow - snapshot.TimestampUtc).TotalMilliseconds);

                var summary = await HandleSignalAsync(
                    entrySignal,
                    _options.MaxOrdersPerCycle - ordersSubmitted,
                    cancellationToken).ConfigureAwait(false);
                ordersSubmitted += summary.Submitted;
                if (ordersSubmitted >= _options.MaxOrdersPerCycle)
                {
                    break;
                }
            }

            var exitSignal = await _strategy
                .EvaluateExitAsync(snapshot, cancellationToken)
                .ConfigureAwait(false);

            await LogSignalObservationAsync("Exit", snapshot, exitSignal, cancellationToken)
                .ConfigureAwait(false);

            if (exitSignal is not null)
            {
                StrategyMetrics.RecordDecisionLatency(
                    _strategy.Id,
                    (DateTimeOffset.UtcNow - snapshot.TimestampUtc).TotalMilliseconds);

                var summary = await HandleSignalAsync(
                    exitSignal,
                    _options.MaxOrdersPerCycle - ordersSubmitted,
                    cancellationToken).ConfigureAwait(false);
                ordersSubmitted += summary.Submitted;
                if (ordersSubmitted >= _options.MaxOrdersPerCycle)
                {
                    break;
                }
            }
        }
    }

    private void UpdateActiveMarkets(IReadOnlyList<string> marketIds)
    {
        lock (_statsLock)
        {
            _activeMarkets.Clear();
            foreach (var id in marketIds)
            {
                _activeMarkets.Add(id);
            }
        }
    }

    private async Task NotifyStatsAsync()
    {
        if (_onStats is not null)
        {
            var markets = GetActiveMarkets();
            await _onStats(markets, _cycleCount, _snapshotsProcessed).ConfigureAwait(false);
        }
    }

    private async Task<SignalExecutionSummary> HandleSignalAsync(
        StrategySignal signal,
        int remainingSubmissions,
        CancellationToken cancellationToken)
    {
        if (signal.Orders.Count == 0 || remainingSubmissions <= 0)
        {
            return SignalExecutionSummary.Empty;
        }

        var correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");

        StrategyMetrics.RecordSignal(_strategy.Id, signal.Type.ToString());
        StrategyMetrics.RecordDecision(_strategy.Id, signal.Type.ToString());
        if (_onDecision is not null)
        {
            await _onDecision(DateTimeOffset.UtcNow).ConfigureAwait(false);
        }

        await _context.DecisionLogger.LogAsync(new StrategyDecision(
            _strategy.Id,
            signal.Type.ToString(),
            signal.Reason,
            signal.MarketId,
            signal.ContextJson,
            DateTimeOffset.UtcNow,
            CorrelationId: correlationId), cancellationToken).ConfigureAwait(false);

        var pendingOrders = new List<PendingExecutionOrder>();

        foreach (var intent in signal.Orders)
        {
            if (pendingOrders.Count >= remainingSubmissions)
            {
                break;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var clientOrderId = StrategyOrderIdGenerator.Create(_strategy.Id, intent.MarketId, intent.Leg.ToString());

            var riskRequest = new RiskOrderRequest
            {
                StrategyId = _strategy.Id,
                ClientOrderId = clientOrderId,
                MarketId = intent.MarketId,
                TokenId = intent.TokenId,
                Side = intent.Side,
                OrderType = intent.OrderType,
                TimeInForce = intent.TimeInForce,
                Price = intent.Price,
                Quantity = intent.Quantity,
                Leg = intent.Leg,
                TimestampUtc = DateTimeOffset.UtcNow
            };

            var riskResult = await _context.RiskManager
                .ValidateOrderAsync(riskRequest, cancellationToken)
                .ConfigureAwait(false);

            if (!riskResult.Allowed)
            {
                StrategyMetrics.RecordOrderFailure(_strategy.Id, "risk_reject");
                await LogObservationAsync(
                    "Risk",
                    "Rejected",
                    NormalizeReasonCode(riskResult.Message, "risk_rejected"),
                    intent.MarketId,
                    JsonSerializer.Serialize(new
                    {
                        clientOrderId,
                        intent.Price,
                        intent.Quantity,
                        intent.Side,
                        intent.OrderType,
                        intent.TimeInForce,
                        intent.Leg
                    }, JsonOptions),
                    JsonSerializer.Serialize(new { riskResult.Message }, JsonOptions),
                    correlationId,
                    cancellationToken).ConfigureAwait(false);

                await _context.DecisionLogger.LogAsync(new StrategyDecision(
                    _strategy.Id,
                    "RiskRejected",
                    riskResult.Message ?? "Risk check rejected",
                    intent.MarketId,
                    signal.ContextJson,
                    DateTimeOffset.UtcNow,
                    CorrelationId: correlationId), cancellationToken).ConfigureAwait(false);

                await _context.RiskManager.RecordOrderErrorAsync(
                    _strategy.Id,
                    clientOrderId,
                    "RISK_REJECTED",
                    riskResult.Message ?? "Risk check rejected",
                    cancellationToken).ConfigureAwait(false);

                continue;
            }

            var request = new ExecutionRequest
            {
                ClientOrderId = clientOrderId,
                StrategyId = _strategy.Id,
                CorrelationId = correlationId,
                MarketId = intent.MarketId,
                TokenId = intent.TokenId,
                Outcome = intent.Outcome,
                Side = intent.Side,
                OrderType = intent.OrderType,
                TimeInForce = intent.TimeInForce,
                Price = intent.Price,
                Quantity = intent.Quantity,
                NegRisk = intent.NegRisk
            };

            pendingOrders.Add(new PendingExecutionOrder(intent, riskRequest, request));
        }

        if (pendingOrders.Count == 0)
        {
            return SignalExecutionSummary.Empty;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var executionResults = await _context.ExecutionService
            .PlaceOrdersAsync(pendingOrders.Select(order => order.ExecutionRequest).ToList(), cancellationToken)
            .ConfigureAwait(false);

        if (executionResults.Count != pendingOrders.Count)
        {
            _logger.LogError(
                "Execution service returned {ResultCount} results for {RequestCount} requests for strategy {StrategyId}",
                executionResults.Count,
                pendingOrders.Count,
                _strategy.Id);
        }

        for (var index = 0; index < pendingOrders.Count; index++)
        {
            var pendingOrder = pendingOrders[index];
            var intent = pendingOrder.Intent;
            var riskRequest = pendingOrder.RiskRequest;
            var clientOrderId = pendingOrder.ExecutionRequest.ClientOrderId;
            var result = index < executionResults.Count
                ? executionResults[index]
                : ExecutionResult.Fail(
                    clientOrderId,
                    "BATCH_RESULT_MISSING",
                    "Execution batch did not return a result for this order");

            if (!result.Success)
            {
                StrategyMetrics.RecordOrderFailure(_strategy.Id, result.ErrorCode ?? "execution_error");
                await LogObservationAsync(
                    "Execution",
                    "Rejected",
                    NormalizeReasonCode(result.ErrorCode ?? result.ErrorMessage, "execution_rejected"),
                    intent.MarketId,
                    JsonSerializer.Serialize(new
                    {
                        clientOrderId,
                        intent.Price,
                        intent.Quantity,
                        intent.Side,
                        intent.OrderType,
                        intent.TimeInForce,
                        intent.Leg
                    }, JsonOptions),
                    JsonSerializer.Serialize(new { result.ErrorCode, result.ErrorMessage }, JsonOptions),
                    correlationId,
                    cancellationToken).ConfigureAwait(false);

                await _context.RiskManager.RecordOrderErrorAsync(
                    _strategy.Id,
                    clientOrderId,
                    result.ErrorCode ?? "EXECUTION_ERROR",
                    result.ErrorMessage ?? "Execution failed",
                    cancellationToken).ConfigureAwait(false);

                await _context.DecisionLogger.LogAsync(new StrategyDecision(
                    _strategy.Id,
                    "OrderRejected",
                    result.ErrorMessage ?? "Execution failed",
                    intent.MarketId,
                    signal.ContextJson,
                    DateTimeOffset.UtcNow,
                    CorrelationId: correlationId), cancellationToken).ConfigureAwait(false);

                continue;
            }

            StrategyMetrics.RecordOrderPlaced(_strategy.Id);
            await LogObservationAsync(
                "Execution",
                "Accepted",
                "order_accepted",
                intent.MarketId,
                JsonSerializer.Serialize(new
                {
                    clientOrderId,
                    intent.Price,
                    intent.Quantity,
                    intent.Side,
                    intent.OrderType,
                    intent.TimeInForce,
                    intent.Leg,
                    result.Status,
                    result.FilledQuantity
                }, JsonOptions),
                signal.ContextJson,
                correlationId,
                cancellationToken).ConfigureAwait(false);

            _orderRegistry.Register(clientOrderId, new StrategyOrderInfo(
                _strategy.Id,
                intent.MarketId,
                intent.TokenId,
                intent.Outcome,
                intent.Leg,
                signal.Type,
                intent.Side,
                intent.OrderType,
                intent.TimeInForce,
                intent.Price,
                intent.Quantity,
                DateTimeOffset.UtcNow,
                result.Status,
                result.FilledQuantity));

            await _context.RiskManager
                .RecordOrderAcceptedAsync(riskRequest, cancellationToken)
                .ConfigureAwait(false);

            var update = new StrategyOrderUpdate(
                _strategy.Id,
                clientOrderId,
                intent.MarketId,
                intent.TokenId,
                intent.Outcome,
                intent.Leg,
                signal.Type,
                intent.Side,
                intent.OrderType,
                intent.TimeInForce,
                intent.Price,
                result.Status,
                result.FilledQuantity,
                intent.Quantity,
                result.TimestampUtc);

            await _strategy.OnOrderUpdateAsync(update, cancellationToken).ConfigureAwait(false);
            await LogObservationAsync(
                "OrderUpdate",
                "Observed",
                NormalizeReasonCode(result.Status.ToString(), "order_update"),
                intent.MarketId,
                JsonSerializer.Serialize(new
                {
                    clientOrderId,
                    result.Status,
                    result.FilledQuantity,
                    originalQuantity = intent.Quantity
                }, JsonOptions),
                signal.ContextJson,
                correlationId,
                cancellationToken).ConfigureAwait(false);

            await _context.RiskManager.RecordOrderUpdateAsync(new RiskOrderUpdate
            {
                ClientOrderId = clientOrderId,
                Status = result.Status,
                FilledQuantity = result.FilledQuantity,
                OriginalQuantity = intent.Quantity
            }, cancellationToken).ConfigureAwait(false);
        }

        return new SignalExecutionSummary(pendingOrders.Count);
    }

    private async Task LogMarketSelectionAsync(
        IReadOnlyList<string> marketIdList,
        CancellationToken cancellationToken)
    {
        await LogObservationAsync(
            "Select",
            marketIdList.Count == 0 ? "Skipped" : "Selected",
            marketIdList.Count == 0 ? "no_markets_selected" : "markets_selected",
            null,
            JsonSerializer.Serialize(new
            {
                selectedMarketCount = marketIdList.Count,
                selectedMarkets = marketIdList.Take(50).ToArray()
            }, JsonOptions),
            null,
            null,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task LogSignalObservationAsync(
        string phase,
        MarketSnapshot snapshot,
        StrategySignal? signal,
        CancellationToken cancellationToken)
    {
        var outcome = signal is null ? "Skipped" : "Signal";
        var reasonCode = signal is null
            ? $"no_{phase.ToLowerInvariant()}_signal"
            : NormalizeReasonCode(signal.Reason, $"{phase.ToLowerInvariant()}_signal");

        await LogObservationAsync(
            phase,
            outcome,
            reasonCode,
            snapshot.MarketId,
            BuildSnapshotFeaturesJson(snapshot),
            signal?.ContextJson,
            null,
            cancellationToken).ConfigureAwait(false);
    }

    private Task LogObservationAsync(
        string phase,
        string outcome,
        string reasonCode,
        string? marketId,
        string? featuresJson,
        string? stateJson,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        return _context.ObservationLogger.LogAsync(
            new StrategyObservation(
                _strategy.Id,
                marketId,
                phase,
                outcome,
                reasonCode,
                featuresJson,
                stateJson,
                correlationId,
                _options.ConfigVersion,
                null,
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private static string BuildSnapshotFeaturesJson(MarketSnapshot snapshot)
    {
        return JsonSerializer.Serialize(new
        {
            snapshot.MarketId,
            snapshot.TimestampUtc,
            yes = ToTopBookFeature(snapshot.YesTopOfBook),
            no = ToTopBookFeature(snapshot.NoTopOfBook)
        }, JsonOptions);
    }

    private static object? ToTopBookFeature(Autotrade.MarketData.Application.Contract.OrderBook.TopOfBookDto? topBook)
    {
        return topBook is null
            ? null
            : new
            {
                topBook.AssetId,
                bestBidPrice = topBook.BestBidPrice?.ToString(),
                bestBidSize = topBook.BestBidSize?.ToString(),
                bestAskPrice = topBook.BestAskPrice?.ToString(),
                bestAskSize = topBook.BestAskSize?.ToString(),
                topBook.Spread,
                topBook.LastUpdatedUtc
            };
    }

    private static string NormalizeReasonCode(string? value, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var chars = source
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray();
        var normalized = new string(chars).Trim('_');
        while (normalized.Contains("__", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = fallback;
        }

        return normalized.Length <= 128 ? normalized : normalized[..128].TrimEnd('_');
    }

    private sealed record SignalExecutionSummary(int Submitted)
    {
        public static SignalExecutionSummary Empty { get; } = new(0);
    }

    private sealed record PendingExecutionOrder(
        StrategyOrderIntent Intent,
        RiskOrderRequest RiskRequest,
        ExecutionRequest ExecutionRequest);
}
