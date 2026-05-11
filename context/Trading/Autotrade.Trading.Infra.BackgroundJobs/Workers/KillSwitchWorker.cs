// ============================================================================
// Kill Switch 执行服务
// ============================================================================
// 后台服务，持续监控 Kill Switch 状态并执行相应动作：
// - SoftStop: 阻止新订单，但保留现有订单
// - HardStop: 阻止新订单，并撤销所有现有订单
// 
// 支持全局和策略级别的 Kill Switch。
// ============================================================================

using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Application.Risk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Trading.Infra.BackgroundJobs.Workers;

/// <summary>
/// Kill Switch 执行服务。
/// 后台服务，持续监控并执行 Kill Switch 指令。
/// </summary>
public sealed class KillSwitchWorker : BackgroundService
{
    private readonly IRiskManager _riskManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<KillSwitchWorker> _logger;
    private readonly IOptionsMonitor<KillSwitchControlOptions> _controlOptions;
    private readonly HashSet<string> _handledKillSwitches = new();
    private string? _lastGlobalResetToken;
    private readonly Dictionary<string, string?> _lastStrategyResetTokens = new(StringComparer.OrdinalIgnoreCase);

    public KillSwitchWorker(
        IRiskManager riskManager,
        IServiceProvider serviceProvider,
        IOptionsMonitor<KillSwitchControlOptions> controlOptions,
        ILogger<KillSwitchWorker> logger)
    {
        _riskManager = riskManager ?? throw new ArgumentNullException(nameof(riskManager));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _controlOptions = controlOptions ?? throw new ArgumentNullException(nameof(controlOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ApplyDesiredKillSwitchesAsync(stoppingToken).ConfigureAwait(false);

                var activeKillSwitches = _riskManager.GetAllActiveKillSwitches();

                foreach (var state in activeKillSwitches)
                {
                    await HandleKillSwitchAsync(state, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Kill switch enforcement failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ApplyDesiredKillSwitchesAsync(CancellationToken cancellationToken)
    {
        var desired = _controlOptions.CurrentValue;

        // Global reset is an explicit command (token-based); do not auto-reset risk-triggered kill switches.
        if (!string.IsNullOrWhiteSpace(desired.GlobalResetToken) &&
            !string.Equals(desired.GlobalResetToken, _lastGlobalResetToken, StringComparison.Ordinal))
        {
            await _riskManager.ResetKillSwitchAsync(null, cancellationToken).ConfigureAwait(false);
            _lastGlobalResetToken = desired.GlobalResetToken;
        }

        // Global activation (manual control plane)
        if (desired.GlobalActive)
        {
            await _riskManager.ActivateKillSwitchAsync(
                desired.GlobalLevel,
                desired.GlobalReasonCode,
                desired.GlobalReason,
                desired.GlobalContextJson,
                cancellationToken).ConfigureAwait(false);
        }

        // Strategy desired states (best-effort, driven by config keys present)
        foreach (var (strategyId, s) in desired.Strategies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(strategyId))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(s.ResetToken))
            {
                var last = _lastStrategyResetTokens.TryGetValue(strategyId, out var existing) ? existing : null;
                if (!string.Equals(s.ResetToken, last, StringComparison.Ordinal))
                {
                    await _riskManager.ResetKillSwitchAsync(strategyId, cancellationToken).ConfigureAwait(false);
                    _lastStrategyResetTokens[strategyId] = s.ResetToken;
                }
            }

            if (s.Active)
            {
                await _riskManager.ActivateStrategyKillSwitchAsync(
                    strategyId,
                    s.Level,
                    s.ReasonCode,
                    s.Reason,
                    s.MarketId,
                    s.ContextJson,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleKillSwitchAsync(KillSwitchState state, CancellationToken cancellationToken)
    {
        if (!state.IsActive || !state.ActivatedAtUtc.HasValue)
        {
            return;
        }

        // 生成唯一键以跟踪是否已处理
        var key = $"{state.StrategyId ?? "global"}:{state.ActivatedAtUtc:o}";

        if (_handledKillSwitches.Contains(key))
        {
            return;
        }

        _logger.LogWarning(
            "Handling kill switch [{Level}]: Strategy={StrategyId}, Reason={ReasonCode} - {Reason}",
            state.Level,
            state.StrategyId ?? "global",
            state.ReasonCode,
            state.Reason);

        // SoftStop 只阻止新下单，不撤销现有订单
        if (state.Level == KillSwitchLevel.SoftStop)
        {
            _logger.LogInformation(
                "SoftStop kill switch active for {Scope}, new orders blocked but existing orders preserved",
                state.StrategyId ?? "global");
            _handledKillSwitches.Add(key);
            return;
        }

        // HardStop 需要撤销所有现有订单
        if (state.Level == KillSwitchLevel.HardStop)
        {
            await CancelOrdersAsync(state.StrategyId, cancellationToken).ConfigureAwait(false);
            _handledKillSwitches.Add(key);
        }
    }

    private async Task CancelOrdersAsync(string? strategyId, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> orderIds;

        if (string.IsNullOrWhiteSpace(strategyId))
        {
            // 全局 Kill Switch：撤销所有订单
            orderIds = _riskManager.GetOpenOrderIds();
            _logger.LogWarning("HardStop global kill switch: cancelling all {Count} open orders", orderIds.Count);
        }
        else
        {
            // 策略级别 Kill Switch：只撤销该策略的订单
            orderIds = _riskManager.GetOpenOrderIds(strategyId);
            _logger.LogWarning(
                "HardStop strategy kill switch for {StrategyId}: cancelling {Count} open orders",
                strategyId, orderIds.Count);
        }

        if (orderIds.Count == 0)
        {
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var executionService = scope.ServiceProvider.GetRequiredService<IExecutionService>();

        var cancelledCount = 0;
        var failedCount = 0;

        foreach (var clientOrderId in orderIds)
        {
            if (string.IsNullOrWhiteSpace(clientOrderId))
            {
                continue;
            }

            try
            {
                var result = await executionService.CancelOrderAsync(clientOrderId, cancellationToken)
                    .ConfigureAwait(false);

                if (result.Success)
                {
                    cancelledCount++;
                    _logger.LogDebug("Kill switch cancelled order: {ClientOrderId}", clientOrderId);
                }
                else
                {
                    failedCount++;
                    _logger.LogWarning(
                        "Failed to cancel order during kill switch: {ClientOrderId}, Error: {Error}",
                        clientOrderId, result.ErrorMessage);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failedCount++;
                _logger.LogWarning(ex, "Exception cancelling order during kill switch: {ClientOrderId}", clientOrderId);
            }
        }

        _logger.LogInformation(
            "Kill switch order cancellation completed: Cancelled={Cancelled}, Failed={Failed}",
            cancelledCount, failedCount);
    }
}
