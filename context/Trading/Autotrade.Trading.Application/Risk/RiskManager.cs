// ============================================================================
// 风险管理器
// ============================================================================
// 核心风险控制组件，负责：
// - 订单风险校验（资金限制、订单数限制、市场限制等）
// - Kill Switch 管理（全局和策略级别）
// - 未对冲敞口追踪和超时处理
// - 连续错误监控和自动熔断
// - Post-trade 风险再评估
// - 日亏损限制监控
// ============================================================================

using System.Diagnostics;
using System.Text.Json;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Trading.Application.Risk;

/// <summary>
/// 风险管理器实现。
/// 提供全面的风险控制功能，支持全局和策略级别的 SoftStop/HardStop Kill Switch。
/// </summary>
public sealed class RiskManager : IRiskManager
{
    private readonly RiskOptions _options;
    private readonly IRiskCapitalProvider _capitalProvider;
    private readonly IRiskEventRepository _eventRepository;
    private readonly RiskStateStore _stateStore;
    private readonly RiskMetrics _metrics;
    private readonly ILogger<RiskManager> _logger;

    public RiskManager(
        IOptions<RiskOptions> options,
        IRiskCapitalProvider capitalProvider,
        IRiskEventRepository eventRepository,
        RiskStateStore stateStore,
        RiskMetrics metrics,
        ILogger<RiskManager> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _capitalProvider = capitalProvider ?? throw new ArgumentNullException(nameof(capitalProvider));
        _eventRepository = eventRepository ?? throw new ArgumentNullException(nameof(eventRepository));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 启动时校验配置
        _options.Validate();
    }

    public bool IsKillSwitchActive => _stateStore.GetGlobalKillSwitch().IsActive;

    public KillSwitchState GetKillSwitchState() => _stateStore.GetGlobalKillSwitch();

    public KillSwitchState GetStrategyKillSwitchState(string strategyId)
        => _stateStore.GetStrategyKillSwitch(strategyId);

    public bool IsStrategyBlocked(string strategyId)
        => _stateStore.IsStrategyBlocked(strategyId);

    public IReadOnlyList<KillSwitchState> GetAllActiveKillSwitches()
        => _stateStore.GetAllActiveKillSwitches();

    public IReadOnlyList<string> GetOpenOrderIds() => _stateStore.GetOpenOrderIds();

    public IReadOnlyList<string> GetOpenOrderIds(string strategyId) => _stateStore.GetOpenOrderIds(strategyId);

    public async Task<RiskCheckResult> ValidateOrderAsync(
        RiskOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sw = Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrWhiteSpace(request.StrategyId))
            {
                return await RejectAsync("RISK_INVALID_STRATEGY", RiskSeverity.Warning, "StrategyId is required",
                    cancellationToken).ConfigureAwait(false);
            }

            // 检查全局 Kill Switch
            if (_options.KillSwitchEnabled)
            {
                var globalState = _stateStore.GetGlobalKillSwitch();
                if (globalState.IsActive)
                {
                    return await RejectAsync(
                        "RISK_KILL_SWITCH",
                        RiskSeverity.Critical,
                        $"Global kill switch is active: {globalState.Reason}",
                        cancellationToken,
                        RiskAction.KillSwitch).ConfigureAwait(false);
                }

                // 检查策略级别 Kill Switch
                var strategyState = _stateStore.GetStrategyKillSwitch(request.StrategyId);
                if (strategyState.IsActive)
                {
                    return await RejectAsync(
                        "RISK_STRATEGY_KILL_SWITCH",
                        RiskSeverity.Critical,
                        $"Strategy kill switch is active for {request.StrategyId}: {strategyState.Reason}",
                        cancellationToken,
                        RiskAction.KillSwitch).ConfigureAwait(false);
                }
            }

            var snapshot = _stateStore.GetSnapshot();
            var capital = _capitalProvider.GetSnapshot();
            var totalCapital = capital.TotalCapital;

            // 更新指标
            UpdateMetricsGauges(snapshot, capital);

            if (_options.MaxOpenOrders > 0 && snapshot.TotalOpenOrders + 1 > _options.MaxOpenOrders)
            {
                return await RejectAsync(
                    "RISK_MAX_OPEN_ORDERS",
                    RiskSeverity.Warning,
                    $"MaxOpenOrders exceeded ({snapshot.TotalOpenOrders}/{_options.MaxOpenOrders})",
                    cancellationToken).ConfigureAwait(false);
            }

            if (_options.MaxConcurrentMarketsPerStrategy > 0)
            {
                if (!snapshot.MarketsByStrategy.TryGetValue(request.StrategyId, out var markets))
                {
                    markets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                var isNewMarket = !markets.Contains(request.MarketId);
                if (isNewMarket && markets.Count >= _options.MaxConcurrentMarketsPerStrategy)
                {
                    return await RejectAsync(
                        "RISK_MAX_MARKETS_PER_STRATEGY",
                        RiskSeverity.Warning,
                        $"MaxConcurrentMarketsPerStrategy exceeded ({markets.Count}/{_options.MaxConcurrentMarketsPerStrategy})",
                        cancellationToken).ConfigureAwait(false);
                }
            }

            if (totalCapital > 0m)
            {
                var marketOpenNotional = snapshot.OpenNotionalByMarket.TryGetValue(request.MarketId, out var m) ? m : 0m;
                var strategyOpenNotional = snapshot.OpenNotionalByStrategy.TryGetValue(request.StrategyId, out var s) ? s : 0m;
                var totalOpenNotional = snapshot.TotalOpenNotional;

                if (_options.MaxCapitalPerMarket > 0m && marketOpenNotional + request.Notional > totalCapital * _options.MaxCapitalPerMarket)
                {
                    return await RejectAsync(
                        "RISK_MAX_CAPITAL_PER_MARKET",
                        RiskSeverity.Warning,
                        $"Market capital usage limit exceeded: {marketOpenNotional + request.Notional:F2} > {totalCapital * _options.MaxCapitalPerMarket:F2}",
                        cancellationToken).ConfigureAwait(false);
                }

                if (_options.MaxCapitalPerStrategy > 0m && strategyOpenNotional + request.Notional > totalCapital * _options.MaxCapitalPerStrategy)
                {
                    return await RejectAsync(
                        "RISK_MAX_CAPITAL_PER_STRATEGY",
                        RiskSeverity.Warning,
                        $"Strategy capital usage limit exceeded: {strategyOpenNotional + request.Notional:F2} > {totalCapital * _options.MaxCapitalPerStrategy:F2}",
                        cancellationToken).ConfigureAwait(false);
                }

                if (_options.MaxTotalCapitalUtilization > 0m && totalOpenNotional + request.Notional > totalCapital * _options.MaxTotalCapitalUtilization)
                {
                    return await RejectAsync(
                        "RISK_MAX_TOTAL_UTILIZATION",
                        RiskSeverity.Warning,
                        $"Total capital utilization limit exceeded: {totalOpenNotional + request.Notional:F2} > {totalCapital * _options.MaxTotalCapitalUtilization:F2}",
                        cancellationToken).ConfigureAwait(false);
                }

                if (_options.MaxUnhedgedCapitalPerMarket > 0m &&
                    request.Leg == OrderLeg.First &&
                    request.Side == OrderSide.Buy)
                {
                    var existingUnhedged = snapshot.UnhedgedExposures
                        .Where(x => x.MarketId.Equals(request.MarketId, StringComparison.OrdinalIgnoreCase))
                        .Sum(x => x.Notional);

                    if (existingUnhedged + request.Notional > totalCapital * _options.MaxUnhedgedCapitalPerMarket)
                    {
                        return await RejectAsync(
                            "RISK_MAX_UNHEDGED_CAPITAL",
                            RiskSeverity.Warning,
                            $"Unhedged capital per market limit exceeded: {existingUnhedged + request.Notional:F2} > {totalCapital * _options.MaxUnhedgedCapitalPerMarket:F2}",
                            cancellationToken).ConfigureAwait(false);
                    }
                }

                if (_options.DailyLossLimitPct.HasValue && _options.DailyLossLimitPct.Value > 0m)
                {
                    var maxLoss = totalCapital * _options.DailyLossLimitPct.Value;
                    if (capital.RealizedDailyPnl < 0m && Math.Abs(capital.RealizedDailyPnl) > maxLoss)
                    {
                        // 触发 Kill Switch
                        await ActivateKillSwitchAsync(
                            _options.DefaultKillSwitchLevel,
                            "RISK_DAILY_LOSS_LIMIT",
                            $"Daily loss limit exceeded: {Math.Abs(capital.RealizedDailyPnl):F2} > {maxLoss:F2}",
                            cancellationToken: cancellationToken).ConfigureAwait(false);

                        return await RejectAsync(
                            "RISK_DAILY_LOSS_LIMIT",
                            RiskSeverity.Critical,
                            "Daily loss limit exceeded",
                            cancellationToken).ConfigureAwait(false);
                    }
                }

                // 检查资本利用率警告
                var utilization = totalOpenNotional / totalCapital;
                if (utilization >= _options.CapitalUtilizationWarningThreshold)
                {
                    _logger.LogWarning(
                        "Capital utilization warning: {Utilization:P2} >= {Threshold:P2}",
                        utilization, _options.CapitalUtilizationWarningThreshold);
                }
            }

            if (_options.MaxConsecutiveOrderErrors.HasValue)
            {
                if (snapshot.ConsecutiveOrderErrors.TryGetValue(request.StrategyId, out var errors) &&
                    errors >= _options.MaxConsecutiveOrderErrors.Value)
                {
                    await ActivateStrategyKillSwitchAsync(
                        request.StrategyId,
                        _options.DefaultKillSwitchLevel,
                        "RISK_CONSECUTIVE_ERRORS",
                        "Consecutive order errors exceeded",
                        cancellationToken: cancellationToken).ConfigureAwait(false);

                    return await RejectAsync(
                        "RISK_CONSECUTIVE_ERRORS",
                        RiskSeverity.Critical,
                        "Consecutive order errors exceeded",
                        cancellationToken,
                        RiskAction.KillSwitch).ConfigureAwait(false);
                }
            }

            return RiskCheckResult.Allow();
        }
        finally
        {
            sw.Stop();
            _metrics.RecordOrderValidationDuration(sw.Elapsed.TotalMilliseconds, request.StrategyId);
        }
    }

    public Task RecordOrderAcceptedAsync(RiskOrderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entry = new RiskOrderEntry(
            request.StrategyId,
            request.ClientOrderId,
            request.MarketId,
            request.TokenId,
            request.Price,
            request.Quantity,
            0m,
            request.Leg,
            request.Notional,
            DateTimeOffset.UtcNow);

        _stateStore.AddOrder(entry);
        _stateStore.ResetOrderErrors(request.StrategyId);
        return Task.CompletedTask;
    }

    public async Task RecordOrderUpdateAsync(RiskOrderUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        _stateStore.UpdateOrder(update);

        // Post-trade 风险再评估
        if (_options.EnablePostTradeRiskCheck)
        {
            await ExecutePostTradeRiskCheckAsync(update, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ExecutePostTradeRiskCheckAsync(RiskOrderUpdate update, CancellationToken cancellationToken)
    {
        var order = _stateStore.GetOrder(update.ClientOrderId);
        if (order is null)
        {
            // 订单可能已被移除（终态）
            _metrics.IncrementPostTradeChecks(update.StrategyId ?? "unknown", false);
            return;
        }

        var snapshot = _stateStore.GetSnapshot();
        var capital = _capitalProvider.GetSnapshot();
        var totalCapital = capital.TotalCapital;

        if (totalCapital <= 0m)
        {
            _metrics.IncrementPostTradeChecks(order.StrategyId, false);
            return;
        }

        var triggered = false;

        // 检查策略资本利用率
        if (_options.MaxCapitalPerStrategy > 0m)
        {
            var strategyNotional = snapshot.OpenNotionalByStrategy.TryGetValue(order.StrategyId, out var n) ? n : 0m;
            if (strategyNotional > totalCapital * _options.MaxCapitalPerStrategy * 1.1m) // 10% 容错
            {
                _logger.LogWarning(
                    "Post-trade check: Strategy {StrategyId} capital usage {Notional:F2} exceeds limit {Limit:F2}",
                    order.StrategyId, strategyNotional, totalCapital * _options.MaxCapitalPerStrategy);
                triggered = true;
            }
        }

        // 检查全局资本利用率
        if (_options.MaxTotalCapitalUtilization > 0m)
        {
            var utilization = snapshot.TotalOpenNotional / totalCapital;
            if (utilization > _options.MaxTotalCapitalUtilization * 1.1m)
            {
                _logger.LogWarning(
                    "Post-trade check: Total capital utilization {Utilization:P2} exceeds limit {Limit:P2}",
                    utilization, _options.MaxTotalCapitalUtilization);
                triggered = true;
            }
        }

        // 检查日亏损
        if (_options.DailyLossLimitPct.HasValue && _options.DailyLossLimitPct.Value > 0m)
        {
            var maxLoss = totalCapital * _options.DailyLossLimitPct.Value;
            if (capital.RealizedDailyPnl < 0m && Math.Abs(capital.RealizedDailyPnl) > maxLoss)
            {
                _logger.LogError(
                    "Post-trade check: Daily loss {Loss:F2} exceeds limit {Limit:F2}, activating kill switch",
                    Math.Abs(capital.RealizedDailyPnl), maxLoss);

                await ActivateKillSwitchAsync(
                    KillSwitchLevel.HardStop,
                    "RISK_DAILY_LOSS_LIMIT_POSTTRADE",
                    $"Daily loss limit exceeded in post-trade check: {Math.Abs(capital.RealizedDailyPnl):F2}",
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                triggered = true;
            }
        }

        _metrics.IncrementPostTradeChecks(order.StrategyId, triggered);
    }

    public async Task RecordOrderErrorAsync(
        string strategyId,
        string clientOrderId,
        string errorCode,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(strategyId))
        {
            return;
        }

        _stateStore.RecordOrderError(strategyId);
        _metrics.IncrementOrderErrors(strategyId);

        await RecordRiskEventAsync(
            errorCode,
            RiskSeverity.Warning,
            $"Order error for {clientOrderId}: {message}",
            strategyId,
            cancellationToken).ConfigureAwait(false);

        if (_options.MaxConsecutiveOrderErrors.HasValue)
        {
            var snapshot = _stateStore.GetSnapshot();
            if (snapshot.ConsecutiveOrderErrors.TryGetValue(strategyId, out var errors) &&
                errors >= _options.MaxConsecutiveOrderErrors.Value)
            {
                await ActivateStrategyKillSwitchAsync(
                    strategyId,
                    _options.DefaultKillSwitchLevel,
                    "RISK_CONSECUTIVE_ERRORS",
                    $"Consecutive order errors exceeded ({errors})",
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
    }

    #region Kill Switch

    public async Task ActivateKillSwitchAsync(
        KillSwitchLevel level,
        string reasonCode,
        string reason,
        string? contextJson = null,
        CancellationToken cancellationToken = default)
    {
        if (!_options.KillSwitchEnabled)
        {
            _logger.LogWarning("Kill switch is disabled, ignoring activation request: {Reason}", reason);
            return;
        }

        var currentState = _stateStore.GetGlobalKillSwitch();
        if (currentState.IsActive && currentState.Level >= level)
        {
            // 已经激活且级别相同或更高
            return;
        }

        var newState = new KillSwitchState
        {
            IsActive = true,
            Level = level,
            ReasonCode = reasonCode,
            Reason = reason,
            ActivatedAtUtc = DateTimeOffset.UtcNow,
            ContextJson = contextJson
        };

        _stateStore.SetGlobalKillSwitch(newState);
        _metrics.IncrementKillSwitchActivations(level, reasonCode);
        _logger.LogError("Global kill switch activated [{Level}]: {ReasonCode} - {Reason}", level, reasonCode, reason);

        await RecordRiskEventAsync(reasonCode, RiskSeverity.Critical, reason, null, cancellationToken, contextJson)
            .ConfigureAwait(false);
    }

    // 兼容旧接口
    public Task ActivateKillSwitchAsync(string reason, CancellationToken cancellationToken = default)
        => ActivateKillSwitchAsync(_options.DefaultKillSwitchLevel, "RISK_KILL_SWITCH", reason, null, cancellationToken);

    public async Task ActivateStrategyKillSwitchAsync(
        string strategyId,
        KillSwitchLevel level,
        string reasonCode,
        string reason,
        string? marketId = null,
        string? contextJson = null,
        CancellationToken cancellationToken = default)
    {
        if (!_options.KillSwitchEnabled)
        {
            _logger.LogWarning("Kill switch is disabled, ignoring strategy activation request: {StrategyId} - {Reason}", strategyId, reason);
            return;
        }

        if (string.IsNullOrWhiteSpace(strategyId))
        {
            throw new ArgumentException("StrategyId cannot be empty", nameof(strategyId));
        }

        var currentState = _stateStore.GetStrategyKillSwitch(strategyId);
        if (currentState.IsActive && currentState.Level >= level)
        {
            return;
        }

        var newState = new KillSwitchState
        {
            IsActive = true,
            Level = level,
            ReasonCode = reasonCode,
            Reason = reason,
            StrategyId = strategyId,
            MarketId = marketId,
            ActivatedAtUtc = DateTimeOffset.UtcNow,
            ContextJson = contextJson
        };

        _stateStore.SetStrategyKillSwitch(strategyId, newState);
        _metrics.IncrementKillSwitchActivations(level, reasonCode);
        _logger.LogError("Strategy kill switch activated [{Level}] for {StrategyId}: {ReasonCode} - {Reason}",
            level, strategyId, reasonCode, reason);

        await RecordRiskEventAsync(reasonCode, RiskSeverity.Critical, reason, strategyId, cancellationToken, contextJson)
            .ConfigureAwait(false);
    }

    public Task ResetKillSwitchAsync(string? strategyId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(strategyId))
        {
            _stateStore.SetGlobalKillSwitch(KillSwitchState.Inactive);
            _logger.LogWarning("Global kill switch has been reset");
        }
        else
        {
            _stateStore.SetStrategyKillSwitch(strategyId, KillSwitchState.Inactive);
            _logger.LogWarning("Strategy kill switch has been reset for {StrategyId}", strategyId);
        }

        return Task.CompletedTask;
    }

    #endregion

    public IReadOnlyList<UnhedgedExposureSnapshot> GetExpiredUnhedgedExposures(DateTimeOffset nowUtc)
    {
        var snapshot = _stateStore.GetSnapshot();
        var maxAge = TimeSpan.FromSeconds(_options.MaxFirstLegExposureSeconds);

        return snapshot.UnhedgedExposures
            .Where(x => nowUtc - x.StartedAtUtc > maxAge)
            .ToList();
    }

    public Task RecordUnhedgedExposureAsync(
        string strategyId,
        string marketId,
        string tokenId,
        string hedgeTokenId,
        OutcomeSide outcome,
        OrderSide side,
        decimal quantity,
        decimal price,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(strategyId) || string.IsNullOrWhiteSpace(marketId) ||
            string.IsNullOrWhiteSpace(tokenId) || string.IsNullOrWhiteSpace(hedgeTokenId))
        {
            return Task.CompletedTask;
        }

        var notional = quantity * price;
        _stateStore.RecordUnhedgedExposure(new UnhedgedExposureSnapshot(
            strategyId, marketId, tokenId, hedgeTokenId, outcome, side, quantity, price, notional, startedAtUtc));
        return Task.CompletedTask;
    }

    public Task ClearUnhedgedExposureAsync(string strategyId, string marketId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(strategyId) || string.IsNullOrWhiteSpace(marketId))
        {
            return Task.CompletedTask;
        }

        _stateStore.ClearUnhedgedExposure(strategyId, marketId);
        return Task.CompletedTask;
    }

    public RiskStateSnapshot GetStateSnapshot()
    {
        var snapshot = _stateStore.GetSnapshot();
        var capital = _capitalProvider.GetSnapshot();

        var utilization = capital.TotalCapital > 0m
            ? snapshot.TotalOpenNotional / capital.TotalCapital
            : 0m;

        return new RiskStateSnapshot(
            snapshot.TotalOpenNotional,
            snapshot.TotalOpenOrders,
            capital.TotalCapital,
            capital.AvailableCapital,
            utilization,
            snapshot.OpenNotionalByStrategy,
            snapshot.OpenNotionalByMarket,
            snapshot.OpenOrdersByStrategy,
            snapshot.UnhedgedExposures);
    }

    private void UpdateMetricsGauges(RiskSnapshot snapshot, RiskCapitalSnapshot capital)
    {
        var utilization = capital.TotalCapital > 0m
            ? (double)(snapshot.TotalOpenNotional / capital.TotalCapital)
            : 0.0;

        var activeKillSwitches = _stateStore.GetAllActiveKillSwitches().Count;

        _metrics.UpdateGauges(
            utilization,
            (double)snapshot.TotalOpenNotional,
            snapshot.TotalOpenOrders,
            activeKillSwitches,
            snapshot.UnhedgedExposures.Count);
    }

    private async Task<RiskCheckResult> RejectAsync(
        string code,
        RiskSeverity severity,
        string message,
        CancellationToken cancellationToken,
        RiskAction action = RiskAction.Block)
    {
        _metrics.IncrementRejections(code);
        _logger.LogWarning("Risk rejection: {Code} - {Message}", code, message);

        await RecordRiskEventAsync(code, severity, message, null, cancellationToken).ConfigureAwait(false);

        return RiskCheckResult.Block(code, message, severity, action);
    }

    private Task RecordRiskEventAsync(
        string code,
        RiskSeverity severity,
        string message,
        string? strategyId,
        CancellationToken cancellationToken,
        string? contextJson = null)
    {
        // 不再依赖 TradingAccountId，直接记录事件
        return _eventRepository.AddAsync(code, severity, message, strategyId, contextJson, cancellationToken);
    }
}
