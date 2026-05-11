// ============================================================================
// 未对冲敞口看门狗
// ============================================================================
// 后台服务，监控未对冲敞口超时并执行配置的退出动作：
// - LogOnly: 仅记录日志
// - CancelOrders: 撤销挂单
// - CancelAndExit: 撤销挂单 + 退出头寸
// - ForceHedge: 撤销挂单 + 强制对冲
// 
// 使用 FOK (Fill-or-Kill) 订单确保快速执行。
// ============================================================================

using System.Text.Json;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Application.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Trading.Infra.BackgroundJobs.Workers;

/// <summary>
/// 未对冲敞口看门狗。
/// 监控超时的未对冲敞口并执行配置的退出动作。
/// </summary>
public sealed class UnhedgedExposureWorker : BackgroundService
{
    private readonly IRiskManager _riskManager;
    private readonly IRiskEventRepository _eventRepository;
    private readonly IServiceProvider _serviceProvider;
    private readonly RiskOptions _options;
    private readonly RiskMetrics _metrics;
    private readonly ILogger<UnhedgedExposureWorker> _logger;

    public UnhedgedExposureWorker(
        IRiskManager riskManager,
        IRiskEventRepository eventRepository,
        IServiceProvider serviceProvider,
        IOptions<RiskOptions> options,
        RiskMetrics metrics,
        ILogger<UnhedgedExposureWorker> logger)
    {
        _riskManager = riskManager ?? throw new ArgumentNullException(nameof(riskManager));
        _eventRepository = eventRepository ?? throw new ArgumentNullException(nameof(eventRepository));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var expired = _riskManager.GetExpiredUnhedgedExposures(DateTimeOffset.UtcNow);
                if (expired.Count > 0)
                {
                    foreach (var exposure in expired)
                    {
                        await HandleExpiredExposureAsync(exposure, stoppingToken).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhedged exposure watchdog failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task HandleExpiredExposureAsync(UnhedgedExposureSnapshot exposure, CancellationToken cancellationToken)
    {
        var contextJson = JsonSerializer.Serialize(new
        {
            exposure.StrategyId,
            exposure.MarketId,
            exposure.TokenId,
            exposure.HedgeTokenId,
            Outcome = exposure.Outcome.ToString(),
            Side = exposure.Side.ToString(),
            exposure.Quantity,
            exposure.Price,
            exposure.Notional,
            StartedAtUtc = exposure.StartedAtUtc.ToString("o"),
            ExpiredAtUtc = DateTimeOffset.UtcNow.ToString("o"),
            ExposureDurationSeconds = (DateTimeOffset.UtcNow - exposure.StartedAtUtc).TotalSeconds,
            ConfiguredTimeoutSeconds = _options.MaxFirstLegExposureSeconds,
            ExitAction = _options.UnhedgedExitAction.ToString()
        });

        _logger.LogWarning(
            "Unhedged exposure timeout: Strategy={StrategyId}, Market={MarketId}, Token={TokenId}, Outcome={Outcome}, Qty={Quantity:F4}, Notional={Notional:F2}, Duration={Duration}s, Action={Action}",
            exposure.StrategyId,
            exposure.MarketId,
            exposure.TokenId,
            exposure.Outcome,
            exposure.Quantity,
            exposure.Notional,
            (DateTimeOffset.UtcNow - exposure.StartedAtUtc).TotalSeconds,
            _options.UnhedgedExitAction);

        _metrics.IncrementUnhedgedExposureTimeouts(exposure.StrategyId, exposure.MarketId);

        switch (_options.UnhedgedExitAction)
        {
            case UnhedgedExitAction.LogOnly:
                // 仅记录日志、指标和 RiskEvent，不触发 kill switch，不阻止新单
                _logger.LogInformation(
                    "LogOnly mode: Unhedged exposure recorded but no action taken for {StrategyId}/{MarketId}",
                    exposure.StrategyId, exposure.MarketId);

                // 显式记录 RiskEvent 用于审计
                await _eventRepository.AddAsync(
                    "RISK_UNHEDGED_TIMEOUT_LOG",
                    RiskSeverity.Warning,
                    $"Unhedged exposure timeout (log only): {exposure.MarketId}, Qty={exposure.Quantity:F4}, Notional={exposure.Notional:F2}",
                    exposure.StrategyId,
                    contextJson,
                    cancellationToken).ConfigureAwait(false);
                break;

            case UnhedgedExitAction.CancelOrders:
                await CancelStrategyOrdersAsync(exposure.StrategyId, exposure.MarketId, cancellationToken)
                    .ConfigureAwait(false);

                await _riskManager.ActivateStrategyKillSwitchAsync(
                    exposure.StrategyId,
                    KillSwitchLevel.SoftStop,
                    "RISK_UNHEDGED_TIMEOUT_CANCEL",
                    $"Unhedged exposure timeout, orders cancelled: {exposure.MarketId}",
                    exposure.MarketId,
                    contextJson,
                    cancellationToken).ConfigureAwait(false);

                _metrics.IncrementExitActions(UnhedgedExitAction.CancelOrders, exposure.StrategyId);
                break;

            case UnhedgedExitAction.CancelAndExit:
                // 1. 撤销所有未完成订单
                await CancelStrategyOrdersAsync(exposure.StrategyId, exposure.MarketId, cancellationToken)
                    .ConfigureAwait(false);

                // 2. 执行退出（以激进限价卖出敞口头寸）
                await ExecuteExitOrderAsync(exposure, cancellationToken).ConfigureAwait(false);

                // 3. 激活 HardStop
                await _riskManager.ActivateStrategyKillSwitchAsync(
                    exposure.StrategyId,
                    KillSwitchLevel.HardStop,
                    "RISK_UNHEDGED_TIMEOUT_EXIT",
                    $"Unhedged exposure timeout, exit executed: {exposure.MarketId}",
                    exposure.MarketId,
                    contextJson,
                    cancellationToken).ConfigureAwait(false);

                _metrics.IncrementExitActions(UnhedgedExitAction.CancelAndExit, exposure.StrategyId);
                break;

            case UnhedgedExitAction.ForceHedge:
                // 1. 撤销所有未完成订单
                await CancelStrategyOrdersAsync(exposure.StrategyId, exposure.MarketId, cancellationToken)
                    .ConfigureAwait(false);

                // 2. 执行强制对冲（以激进限价买入第二腿）
                await ExecuteForceHedgeOrderAsync(exposure, cancellationToken).ConfigureAwait(false);

                // 3. 激活 HardStop
                await _riskManager.ActivateStrategyKillSwitchAsync(
                    exposure.StrategyId,
                    KillSwitchLevel.HardStop,
                    "RISK_UNHEDGED_TIMEOUT_FORCE_HEDGE",
                    $"Unhedged exposure timeout, force hedge executed: {exposure.MarketId}",
                    exposure.MarketId,
                    contextJson,
                    cancellationToken).ConfigureAwait(false);

                _metrics.IncrementExitActions(UnhedgedExitAction.ForceHedge, exposure.StrategyId);
                break;

            default:
                throw new InvalidOperationException($"Unknown UnhedgedExitAction: {_options.UnhedgedExitAction}");
        }

        // 清除敞口记录
        await _riskManager.ClearUnhedgedExposureAsync(exposure.StrategyId, exposure.MarketId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 执行退出订单：以激进限价卖出敞口头寸。
    /// 使用 UnhedgedExposureSnapshot 中的真实 token/outcome/qty 信息。
    /// 注意：Polymarket 不支持市价单，使用 0.01 的激进卖价模拟市价卖出。
    /// </summary>
    private async Task ExecuteExitOrderAsync(UnhedgedExposureSnapshot exposure, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var executionService = scope.ServiceProvider.GetRequiredService<IExecutionService>();

            if (exposure.Quantity <= 0m)
            {
                _logger.LogWarning(
                    "Exit order skipped: No position to exit for {StrategyId}/{MarketId}",
                    exposure.StrategyId, exposure.MarketId);
                return;
            }

            // 使用真实的订单信息：
            // - TokenId: 从敞口记录中获取
            // - Outcome: 与敞口相同的 outcome
            // - Side: 与敞口相反（如果敞口是 Buy，退出就是 Sell）
            // - Quantity: 敞口的真实数量
            var exitSide = exposure.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;

            var exitRequest = new ExecutionRequest
            {
                ClientOrderId = $"EXIT_{exposure.StrategyId}_{exposure.MarketId}_{DateTimeOffset.UtcNow.Ticks}",
                MarketId = exposure.MarketId,
                TokenId = exposure.TokenId,
                Outcome = exposure.Outcome,
                Side = exitSide,
                OrderType = OrderType.Limit,
                TimeInForce = TimeInForce.Fok, // FOK 确保完全成交或取消
                Price = exitSide == OrderSide.Sell ? 0.01m : 0.99m, // 激进价格
                Quantity = exposure.Quantity
            };

            _logger.LogWarning(
                "Executing exit order for unhedged exposure: {StrategyId}/{MarketId}, Token={TokenId}, Outcome={Outcome}, Side={Side}, Qty={Quantity:F4}, Price={Price:F2}",
                exposure.StrategyId, exposure.MarketId, exposure.TokenId, exposure.Outcome, exitSide, exposure.Quantity, exitRequest.Price);

            var result = await executionService.PlaceOrderAsync(exitRequest, cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                _logger.LogInformation(
                    "Exit order placed successfully: {ClientOrderId} for {StrategyId}/{MarketId}",
                    exitRequest.ClientOrderId, exposure.StrategyId, exposure.MarketId);
            }
            else
            {
                _logger.LogError(
                    "Exit order failed: {StrategyId}/{MarketId}, Error: {Error}",
                    exposure.StrategyId, exposure.MarketId, result.ErrorMessage);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Failed to execute exit order for unhedged exposure: {StrategyId}/{MarketId}",
                exposure.StrategyId, exposure.MarketId);
        }
    }

    /// <summary>
    /// 执行强制对冲订单：以激进限价买入第二腿完成对冲。
    /// 使用 UnhedgedExposureSnapshot 中的 HedgeTokenId 来构建对冲订单。
    /// 注意：Polymarket 不支持市价单，使用 0.99 的激进买价模拟市价买入。
    /// </summary>
    private async Task ExecuteForceHedgeOrderAsync(UnhedgedExposureSnapshot exposure, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var executionService = scope.ServiceProvider.GetRequiredService<IExecutionService>();

            if (exposure.Quantity <= 0m)
            {
                _logger.LogWarning(
                    "Force hedge order skipped: No quantity to hedge for {StrategyId}/{MarketId}",
                    exposure.StrategyId, exposure.MarketId);
                return;
            }

            if (string.IsNullOrWhiteSpace(exposure.HedgeTokenId))
            {
                _logger.LogError(
                    "Force hedge order skipped: No hedge token ID provided for {StrategyId}/{MarketId}",
                    exposure.StrategyId, exposure.MarketId);
                return;
            }

            // 对冲逻辑：
            // - 如果第一腿是 Yes + Buy，对冲腿是 No + Buy（买入相反 outcome）
            // - 如果第一腿是 No + Buy，对冲腿是 Yes + Buy
            // - 同样的数量用于对冲
            // - 使用敞口记录时提供的 HedgeTokenId
            var hedgeOutcome = exposure.Outcome == OutcomeSide.Yes ? OutcomeSide.No : OutcomeSide.Yes;

            var hedgeRequest = new ExecutionRequest
            {
                ClientOrderId = $"HEDGE_{exposure.StrategyId}_{exposure.MarketId}_{DateTimeOffset.UtcNow.Ticks}",
                MarketId = exposure.MarketId,
                TokenId = exposure.HedgeTokenId, // 使用记录时提供的对冲 TokenId
                Outcome = hedgeOutcome,
                Side = OrderSide.Buy, // 对冲总是买入
                OrderType = OrderType.Limit,
                TimeInForce = TimeInForce.Fok, // FOK 确保完全成交或取消
                Price = 0.99m, // 激进买价
                Quantity = exposure.Quantity
            };

            _logger.LogWarning(
                "Executing force hedge order for unhedged exposure: {StrategyId}/{MarketId}, Token={TokenId}, Outcome={Outcome}, Side=Buy, Qty={Quantity:F4}, Price=0.99",
                exposure.StrategyId, exposure.MarketId, exposure.HedgeTokenId, hedgeOutcome, exposure.Quantity);

            var result = await executionService.PlaceOrderAsync(hedgeRequest, cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                _logger.LogInformation(
                    "Force hedge order placed successfully: {ClientOrderId} for {StrategyId}/{MarketId}",
                    hedgeRequest.ClientOrderId, exposure.StrategyId, exposure.MarketId);
            }
            else
            {
                _logger.LogError(
                    "Force hedge order failed: {StrategyId}/{MarketId}, Error: {Error}",
                    exposure.StrategyId, exposure.MarketId, result.ErrorMessage);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Failed to execute force hedge order for unhedged exposure: {StrategyId}/{MarketId}",
                exposure.StrategyId, exposure.MarketId);
        }
    }

    private async Task CancelStrategyOrdersAsync(string strategyId, string marketId, CancellationToken cancellationToken)
    {
        var orderIds = _riskManager.GetOpenOrderIds(strategyId);
        if (orderIds.Count == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Cancelling {Count} orders for strategy {StrategyId} due to unhedged exposure timeout",
            orderIds.Count, strategyId);

        using var scope = _serviceProvider.CreateScope();
        var executionService = scope.ServiceProvider.GetRequiredService<IExecutionService>();

        foreach (var clientOrderId in orderIds)
        {
            if (string.IsNullOrWhiteSpace(clientOrderId))
            {
                continue;
            }

            try
            {
                await executionService.CancelOrderAsync(clientOrderId, cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("Cancelled order {ClientOrderId} during unhedged exposure cleanup", clientOrderId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to cancel order during unhedged exposure cleanup: {ClientOrderId}", clientOrderId);
            }
        }
    }
}
