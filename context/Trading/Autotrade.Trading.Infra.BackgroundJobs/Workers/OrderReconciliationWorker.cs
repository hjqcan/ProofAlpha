using Autotrade.Polymarket.Abstractions;
using Autotrade.Polymarket.Models;
using Autotrade.Trading.Application.Audit;
using Autotrade.Trading.Application.Contract.Audit;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.Execution;
using Autotrade.Trading.Application.Metrics;
using Autotrade.Trading.Domain.Shared.Enums;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Trading.Infra.BackgroundJobs.Workers;

/// <summary>
/// 订单对账服务：定期同步交易所订单状态与本地跟踪状态。
/// </summary>
public sealed class OrderReconciliationWorker : BackgroundService
{
    private readonly IPolymarketClobClient _clobClient;
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly IOrderStateTracker _stateTracker;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ExecutionOptions _options;
    private readonly ILogger<OrderReconciliationWorker> _logger;

    public OrderReconciliationWorker(
        IPolymarketClobClient clobClient,
        IIdempotencyStore idempotencyStore,
        IOrderStateTracker stateTracker,
        IServiceScopeFactory scopeFactory,
        IOptions<ExecutionOptions> options,
        ILogger<OrderReconciliationWorker> logger)
    {
        _clobClient = clobClient ?? throw new ArgumentNullException(nameof(clobClient));
        _idempotencyStore = idempotencyStore ?? throw new ArgumentNullException(nameof(idempotencyStore));
        _stateTracker = stateTracker ?? throw new ArgumentNullException(nameof(stateTracker));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableReconciliation)
        {
            _logger.LogInformation("订单对账服务已禁用");
            return;
        }

        // Paper 模式不需要与交易所对账
        if (_options.Mode == ExecutionMode.Paper)
        {
            _logger.LogInformation("Paper 模式下不启用订单对账服务");
            return;
        }

        _logger.LogInformation("订单对账服务已启动，间隔: {Interval}s", _options.ReconciliationIntervalSeconds);

        // 启动时执行一次对账
        await ReconcileOpenOrdersAsync(stoppingToken).ConfigureAwait(false);

        var interval = TimeSpan.FromSeconds(_options.ReconciliationIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
                await ReconcileOpenOrdersAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "订单对账过程中发生异常");
            }
        }

        _logger.LogInformation("订单对账服务已停止");
    }

    private async Task ReconcileOpenOrdersAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("开始订单对账...");

        try
        {
            // 捕获对账开始时的本地状态快照，用于判断状态/成交变化并记录审计事件。
            var localOpenOrdersSnapshot = _stateTracker.GetOpenOrders();
            var localByClientOrderId = localOpenOrdersSnapshot
                .GroupBy(o => o.ClientOrderId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            using var scope = _scopeFactory.CreateScope();
            var auditLogger = scope.ServiceProvider.GetRequiredService<IOrderAuditLogger>();
            var orderRepository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
            var accountContext = scope.ServiceProvider.GetRequiredService<TradingAccountContext>();

            var result = await _clobClient
                .GetOpenOrdersAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!result.IsSuccess || result.Data is null)
            {
                _logger.LogWarning("获取挂单列表失败: {Error}", result.Error?.Message);
                return;
            }

            var openOrders = result.Data;
            _logger.LogDebug("当前挂单数量: {Count}", openOrders.Count);

            var updatedCount = 0;

            foreach (var order in openOrders)
            {
                // 尝试通过交易所订单 ID 找到对应的客户端订单 ID
                var identity = await ResolveOpenOrderIdentityAsync(order, orderRepository, cancellationToken)
                    .ConfigureAwait(false);

                if (identity is null)
                {
                    // 这可能是在本系统之外创建的订单，或者是跟踪记录已过期
                    _logger.LogDebug("未找到交易所订单 {ExchangeOrderId} 的客户端映射", order.Id);
                    continue;
                }

                var clientOrderId = identity.ClientOrderId;
                var trackingEntry = identity.TrackingEntry;

                // 解析订单数量
                var exchangeOriginalQty = TryParseDecimalInvariant(order.OriginalSize, out var oq) ? oq : 0m;
                var exchangeFilledQty = TryParseDecimalInvariant(order.SizeMatched, out var fq) ? fq : 0m;
                var avgPrice = TryParseDecimalInvariant(order.Price, out var ap) ? ap : (decimal?)null;

                // 读取本地上一次状态（用于判断成交增量/终态变化）
                localByClientOrderId.TryGetValue(clientOrderId, out var previousState);

                // 获取审计信息（StrategyId/CorrelationId/MarketId/TokenId）
                // 计算更准确的状态：若已成交但仍 LIVE，则认为 PartiallyFilled
                var mappedStatus = ApplyFilledQuantityToStatus(
                    MapOrderStatus(order.Status),
                    exchangeOriginalQty,
                    exchangeFilledQty);
                var reconciled = await BuildReconciledOrderStateAsync(
                        orderRepository,
                        clientOrderId,
                        order.Id,
                        previousState,
                        exchangeOriginalQty,
                        exchangeFilledQty,
                        mappedStatus,
                        cancellationToken)
                    .ConfigureAwait(false);

                // 更新本地状态
                var stateUpdate = new OrderStateUpdate
                {
                    ClientOrderId = clientOrderId,
                    ExchangeOrderId = order.Id,
                    MarketId = order.Market,
                    TokenId = order.AssetId,
                    Status = reconciled.Status,
                    OriginalQuantity = reconciled.OriginalQuantity,
                    FilledQuantity = reconciled.FilledQuantity,
                    AveragePrice = avgPrice,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };

                await PersistReconciledOrderAsync(
                        orderRepository,
                        reconciled.PersistedOrder,
                        clientOrderId,
                        order.Id,
                        order.Market,
                        order.AssetId,
                        reconciled.FilledQuantity,
                        reconciled.Status,
                        cancellationToken)
                    .ConfigureAwait(false);

                // 审计日志：记录成交/终态（对账得到的信息）
                await TryLogAuditAsync(
                        auditLogger,
                        clientOrderId,
                        orderId: OrderAuditIds.ForClientOrderId(clientOrderId),
                        marketId: ResolveAuditMarketId(order.Market, previousState?.MarketId, trackingEntry?.MarketId),
                        originalQuantity: reconciled.OriginalQuantity,
                        filledQuantity: reconciled.FilledQuantity,
                        averagePrice: avgPrice ?? previousState?.AveragePrice,
                        newStatus: reconciled.Status,
                        previousState: reconciled.PreviousAuditState,
                        trackingEntry,
                        cancellationToken)
                    .ConfigureAwait(false);

                await _stateTracker
                    .OnOrderStateChangedAsync(stateUpdate, cancellationToken)
                    .ConfigureAwait(false);

                await TryLogTradesForOrderAsync(
                        auditLogger,
                        orderRepository,
                        accountContext,
                        clientOrderId,
                        order.Id,
                        order.AssociateTrades,
                        order.Market,
                        trackingEntry,
                        cancellationToken)
                    .ConfigureAwait(false);

                updatedCount++;

                _logger.LogDebug(
                    "订单对账: ClientOrderId={ClientOrderId}, ExchangeOrderId={ExchangeOrderId}, " +
                    "Status={Status}, Matched={Matched}/{Original}",
                    clientOrderId,
                    order.Id,
                    order.Status,
                    order.SizeMatched,
                    order.OriginalSize);
            }

            // 处理本地仍显示为挂单但交易所已不在 open 列表中的订单
            var openOrderIds = new HashSet<string>(openOrders.Select(o => o.Id));
            var localOpenOrders = _stateTracker.GetOpenOrders();

            foreach (var localOrder in localOpenOrders)
            {
                if (string.IsNullOrWhiteSpace(localOrder.ExchangeOrderId))
                {
                    continue;
                }

                if (!openOrderIds.Contains(localOrder.ExchangeOrderId))
                {
                    if (await ReconcileClosedOrderAsync(localOrder, auditLogger, orderRepository, accountContext, cancellationToken).ConfigureAwait(false))
                    {
                        updatedCount++;
                    }
                }
            }

            if (updatedCount > 0)
            {
                _logger.LogInformation("订单对账完成，更新了 {Count} 个订单状态", updatedCount);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "订单对账失败");
        }
    }

    private async Task<OpenOrderIdentity?> ResolveOpenOrderIdentityAsync(
        OrderInfo exchangeOrder,
        IOrderRepository orderRepository,
        CancellationToken cancellationToken)
    {
        var clientOrderId = await _idempotencyStore
            .FindClientOrderIdByExchangeIdAsync(exchangeOrder.Id, cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(clientOrderId))
        {
            var trackingEntry = await _idempotencyStore.GetAsync(clientOrderId, cancellationToken)
                .ConfigureAwait(false);
            if (trackingEntry is not null)
            {
                return new OpenOrderIdentity(clientOrderId, trackingEntry);
            }

            var persistedByClient = await orderRepository
                .GetByClientOrderIdAsync(clientOrderId, cancellationToken)
                .ConfigureAwait(false);
            if (IsActionablePersistedOrder(persistedByClient, exchangeOrder.Id))
            {
                var restored = CreateTrackingEntryFromPersistedOrder(persistedByClient!);
                await _idempotencyStore.SeedAsync(restored, cancellationToken).ConfigureAwait(false);
                return new OpenOrderIdentity(restored.ClientOrderId, restored);
            }

            return new OpenOrderIdentity(clientOrderId, null);
        }

        var persistedByExchange = await orderRepository
            .GetByExchangeOrderIdAsync(exchangeOrder.Id, cancellationToken)
            .ConfigureAwait(false);
        if (!IsActionablePersistedOrder(persistedByExchange, exchangeOrder.Id))
        {
            return null;
        }

        var tracking = CreateTrackingEntryFromPersistedOrder(persistedByExchange!);
        await _idempotencyStore.SeedAsync(tracking, cancellationToken).ConfigureAwait(false);
        return new OpenOrderIdentity(tracking.ClientOrderId, tracking);
    }

    private OrderTrackingEntry CreateTrackingEntryFromPersistedOrder(OrderDto order)
    {
        return new OrderTrackingEntry
        {
            ClientOrderId = order.ClientOrderId!,
            ExchangeOrderId = order.ExchangeOrderId,
            MarketId = order.MarketId,
            TokenId = order.TokenId,
            StrategyId = order.StrategyId,
            CorrelationId = order.CorrelationId,
            OrderSalt = order.OrderSalt,
            OrderTimestamp = order.OrderTimestamp,
            IsUncertainSubmit = order.Status == OrderStatus.Pending &&
                string.IsNullOrWhiteSpace(order.ExchangeOrderId),
            RequestHash = ExecutionRequestHasher.Compute(order),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(3600, _options.IdempotencyTtlSeconds))
        };
    }

    private static bool IsActionablePersistedOrder(OrderDto? order, string exchangeOrderId)
    {
        return order is not null &&
            order.Status is OrderStatus.Pending or OrderStatus.Open or OrderStatus.PartiallyFilled &&
            !string.IsNullOrWhiteSpace(order.ClientOrderId) &&
            !string.IsNullOrWhiteSpace(order.ExchangeOrderId) &&
            string.Equals(order.ExchangeOrderId, exchangeOrderId, StringComparison.Ordinal);
    }

    private sealed record OpenOrderIdentity(string ClientOrderId, OrderTrackingEntry? TrackingEntry);

    private static ExecutionStatus MapOrderStatus(string? status) => status?.ToUpperInvariant() switch
    {
        "LIVE" or "OPEN" => ExecutionStatus.Accepted,
        "MATCHED" or "FILLED" => ExecutionStatus.Filled,
        "CANCELLED" or "CANCELED" => ExecutionStatus.Cancelled,
        "EXPIRED" => ExecutionStatus.Expired,
        _ => ExecutionStatus.Pending
    };

    private async Task<ReconciledOrderState> BuildReconciledOrderStateAsync(
        IOrderRepository orderRepository,
        string clientOrderId,
        string exchangeOrderId,
        OrderStateUpdate? previousState,
        decimal exchangeOriginalQuantity,
        decimal exchangeFilledQuantity,
        ExecutionStatus exchangeStatus,
        CancellationToken cancellationToken)
    {
        var persistedOrder = await ResolvePersistedOrderAsync(
                orderRepository,
                clientOrderId,
                exchangeOrderId,
                cancellationToken)
            .ConfigureAwait(false);

        var originalQuantity = ResolveOriginalQuantity(persistedOrder, previousState, exchangeOriginalQuantity);
        var knownFilled = ResolveKnownFilledQuantity(persistedOrder, previousState, originalQuantity);
        var exchangeFilled = NormalizeFilledQuantity(exchangeFilledQuantity, originalQuantity);
        var filledQuantity = NormalizeFilledQuantity(Math.Max(knownFilled, exchangeFilled), originalQuantity);
        var statusFromFilled = ApplyFilledQuantityToStatus(exchangeStatus, originalQuantity, filledQuantity);
        var currentStatus = ResolveKnownStatus(persistedOrder, previousState);
        var status = MergeExecutionStatus(currentStatus, statusFromFilled);
        var previousAuditState = CreatePreviousAuditState(
            persistedOrder,
            previousState,
            originalQuantity,
            knownFilled,
            currentStatus);

        return new ReconciledOrderState(
            originalQuantity,
            filledQuantity,
            status,
            previousAuditState,
            persistedOrder);
    }

    private static decimal ResolveOriginalQuantity(
        OrderDto? persistedOrder,
        OrderStateUpdate? previousState,
        decimal exchangeOriginalQuantity)
    {
        if (persistedOrder?.Quantity > 0m)
        {
            return persistedOrder.Quantity;
        }

        if (previousState?.OriginalQuantity > 0m)
        {
            return previousState.OriginalQuantity;
        }

        return exchangeOriginalQuantity;
    }

    private static decimal ResolveKnownFilledQuantity(
        OrderDto? persistedOrder,
        OrderStateUpdate? previousState,
        decimal originalQuantity)
    {
        var filledQuantity = Math.Max(
            persistedOrder?.FilledQuantity ?? 0m,
            previousState?.FilledQuantity ?? 0m);

        return NormalizeFilledQuantity(filledQuantity, originalQuantity);
    }

    private static decimal NormalizeFilledQuantity(decimal filledQuantity, decimal originalQuantity)
    {
        var nonNegativeFilled = Math.Max(0m, filledQuantity);
        return originalQuantity > 0m
            ? Math.Min(nonNegativeFilled, originalQuantity)
            : nonNegativeFilled;
    }

    private static ExecutionStatus ResolveKnownStatus(OrderDto? persistedOrder, OrderStateUpdate? previousState)
    {
        var currentStatus = persistedOrder is null
            ? ExecutionStatus.Pending
            : ToExecutionStatus(persistedOrder.Status);

        return previousState is null
            ? currentStatus
            : MergeExecutionStatus(currentStatus, previousState.Status);
    }

    private static ExecutionStatus ApplyFilledQuantityToStatus(
        ExecutionStatus status,
        decimal originalQuantity,
        decimal filledQuantity)
    {
        if (status != ExecutionStatus.Accepted || originalQuantity <= 0m)
        {
            return status;
        }

        if (filledQuantity >= originalQuantity)
        {
            return ExecutionStatus.Filled;
        }

        return filledQuantity > 0m
            ? ExecutionStatus.PartiallyFilled
            : status;
    }

    private static ExecutionStatus MergeExecutionStatus(ExecutionStatus current, ExecutionStatus incoming)
    {
        if (IsTerminalStatus(current))
        {
            return current;
        }

        if (incoming == ExecutionStatus.Pending && current != ExecutionStatus.Pending)
        {
            return current;
        }

        if (incoming == ExecutionStatus.Accepted && current == ExecutionStatus.PartiallyFilled)
        {
            return current;
        }

        return incoming;
    }

    private static bool IsTerminalStatus(ExecutionStatus status) =>
        status is ExecutionStatus.Filled or ExecutionStatus.Cancelled or ExecutionStatus.Rejected or ExecutionStatus.Expired;

    private static OrderStateUpdate? CreatePreviousAuditState(
        OrderDto? persistedOrder,
        OrderStateUpdate? previousState,
        decimal originalQuantity,
        decimal filledQuantity,
        ExecutionStatus status)
    {
        if (previousState is not null)
        {
            return previousState with
            {
                Status = status,
                OriginalQuantity = originalQuantity,
                FilledQuantity = filledQuantity
            };
        }

        if (persistedOrder is null)
        {
            return null;
        }

        return new OrderStateUpdate
        {
            ClientOrderId = persistedOrder.ClientOrderId ?? string.Empty,
            ExchangeOrderId = persistedOrder.ExchangeOrderId ?? string.Empty,
            MarketId = persistedOrder.MarketId,
            TokenId = persistedOrder.TokenId,
            Status = status,
            OriginalQuantity = originalQuantity,
            FilledQuantity = filledQuantity,
            UpdatedAtUtc = persistedOrder.UpdatedAtUtc
        };
    }

    private sealed record ReconciledOrderState(
        decimal OriginalQuantity,
        decimal FilledQuantity,
        ExecutionStatus Status,
        OrderStateUpdate? PreviousAuditState,
        OrderDto? PersistedOrder);

    private static bool TryParseDecimalInvariant(string? raw, out decimal value)
        => decimal.TryParse(
            raw,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);

    private async Task<bool> ReconcileClosedOrderAsync(
        OrderStateUpdate localOrder,
        IOrderAuditLogger auditLogger,
        IOrderRepository orderRepository,
        TradingAccountContext accountContext,
        CancellationToken cancellationToken)
    {
        var exchangeOrderId = localOrder.ExchangeOrderId;
        if (string.IsNullOrWhiteSpace(exchangeOrderId))
        {
            return false;
        }

        var trackingEntry = await _idempotencyStore
            .GetAsync(localOrder.ClientOrderId, cancellationToken)
            .ConfigureAwait(false);

        var result = await _clobClient
            .GetOrderAsync(exchangeOrderId, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess && result.Data is not null)
        {
            var order = result.Data;
            var exchangeOriginalQty = TryParseDecimalInvariant(order.OriginalSize, out var oq) ? oq : localOrder.OriginalQuantity;
            var exchangeFilledQty = TryParseDecimalInvariant(order.SizeMatched, out var fq) ? fq : localOrder.FilledQuantity;
            var avgPrice = TryParseDecimalInvariant(order.Price, out var ap) ? ap : localOrder.AveragePrice;

            // 状态修正：LIVE + size_matched > 0 视为 PartiallyFilled；若完全成交则为 Filled
            var mappedStatus = ApplyFilledQuantityToStatus(
                MapOrderStatus(order.Status),
                exchangeOriginalQty,
                exchangeFilledQty);
            var reconciled = await BuildReconciledOrderStateAsync(
                    orderRepository,
                    localOrder.ClientOrderId,
                    exchangeOrderId,
                    localOrder,
                    exchangeOriginalQty,
                    exchangeFilledQty,
                    mappedStatus,
                    cancellationToken)
                .ConfigureAwait(false);

            await PersistReconciledOrderAsync(
                    orderRepository,
                    reconciled.PersistedOrder,
                    localOrder.ClientOrderId,
                    exchangeOrderId,
                    order.Market,
                    order.AssetId,
                    reconciled.FilledQuantity,
                    reconciled.Status,
                    cancellationToken)
                .ConfigureAwait(false);

            await TryLogAuditAsync(
                    auditLogger,
                    localOrder.ClientOrderId,
                    orderId: OrderAuditIds.ForClientOrderId(localOrder.ClientOrderId),
                    marketId: ResolveAuditMarketId(order.Market, localOrder.MarketId, trackingEntry?.MarketId),
                    originalQuantity: reconciled.OriginalQuantity,
                    filledQuantity: reconciled.FilledQuantity,
                    averagePrice: avgPrice ?? localOrder.AveragePrice,
                    newStatus: reconciled.Status,
                    previousState: reconciled.PreviousAuditState,
                    trackingEntry,
                    cancellationToken)
                .ConfigureAwait(false);

            await TryLogTradesForOrderAsync(
                    auditLogger,
                    orderRepository,
                    accountContext,
                    localOrder.ClientOrderId,
                    exchangeOrderId,
                    order.AssociateTrades,
                    order.Market,
                    trackingEntry,
                    cancellationToken)
                .ConfigureAwait(false);

            await _stateTracker.OnOrderStateChangedAsync(new OrderStateUpdate
            {
                ClientOrderId = localOrder.ClientOrderId,
                ExchangeOrderId = exchangeOrderId,
                MarketId = string.IsNullOrWhiteSpace(order.Market) ? localOrder.MarketId : order.Market,
                TokenId = string.IsNullOrWhiteSpace(order.AssetId) ? localOrder.TokenId : order.AssetId,
                Status = reconciled.Status,
                OriginalQuantity = reconciled.OriginalQuantity,
                FilledQuantity = reconciled.FilledQuantity,
                AveragePrice = avgPrice,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            }, cancellationToken).ConfigureAwait(false);

            _logger.LogDebug(
                "闭环对账: ClientOrderId={ClientOrderId}, ExchangeOrderId={ExchangeOrderId}, Status={Status}",
                localOrder.ClientOrderId,
                exchangeOrderId,
                order.Status);

            return true;
        }

        if (result.StatusCode == 404)
        {
            var fallbackStatus = localOrder.OriginalQuantity > 0m &&
                                 localOrder.FilledQuantity >= localOrder.OriginalQuantity
                ? ExecutionStatus.Filled
                : ExecutionStatus.Cancelled;
            var reconciled = await BuildReconciledOrderStateAsync(
                    orderRepository,
                    localOrder.ClientOrderId,
                    exchangeOrderId,
                    localOrder,
                    localOrder.OriginalQuantity,
                    localOrder.FilledQuantity,
                    fallbackStatus,
                    cancellationToken)
                .ConfigureAwait(false);
            fallbackStatus = reconciled.Status;

            await PersistReconciledOrderAsync(
                    orderRepository,
                    reconciled.PersistedOrder,
                    localOrder.ClientOrderId,
                    exchangeOrderId,
                    localOrder.MarketId,
                    localOrder.TokenId,
                    reconciled.FilledQuantity,
                    fallbackStatus,
                    cancellationToken)
                .ConfigureAwait(false);

            await TryLogAuditAsync(
                    auditLogger,
                    localOrder.ClientOrderId,
                    orderId: OrderAuditIds.ForClientOrderId(localOrder.ClientOrderId),
                    marketId: ResolveAuditMarketId(localOrder.MarketId, localOrder.MarketId, trackingEntry?.MarketId),
                    originalQuantity: reconciled.OriginalQuantity,
                    filledQuantity: reconciled.FilledQuantity,
                    averagePrice: localOrder.AveragePrice,
                    newStatus: fallbackStatus,
                    previousState: reconciled.PreviousAuditState,
                    trackingEntry,
                    cancellationToken)
                .ConfigureAwait(false);

            await _stateTracker.OnOrderStateChangedAsync(new OrderStateUpdate
            {
                ClientOrderId = localOrder.ClientOrderId,
                ExchangeOrderId = exchangeOrderId,
                MarketId = localOrder.MarketId,
                TokenId = localOrder.TokenId,
                Status = fallbackStatus,
                OriginalQuantity = reconciled.OriginalQuantity,
                FilledQuantity = reconciled.FilledQuantity,
                AveragePrice = localOrder.AveragePrice,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            }, cancellationToken).ConfigureAwait(false);

            _logger.LogWarning(
                "订单在交易所未找到，按关闭处理: ClientOrderId={ClientOrderId}, ExchangeOrderId={ExchangeOrderId}, Status={Status}",
                localOrder.ClientOrderId,
                exchangeOrderId,
                fallbackStatus);

            return true;
        }

        _logger.LogWarning(
            "无法确认已关闭订单状态: ClientOrderId={ClientOrderId}, ExchangeOrderId={ExchangeOrderId}, Error={Error}",
            localOrder.ClientOrderId,
            exchangeOrderId,
            result.Error?.Message);

        return false;
    }

    private static string ResolveAuditMarketId(string? exchangeMarketId, string? localMarketId, string? trackedMarketId)
    {
        if (!string.IsNullOrWhiteSpace(trackedMarketId))
        {
            return trackedMarketId;
        }

        if (!string.IsNullOrWhiteSpace(exchangeMarketId))
        {
            return exchangeMarketId;
        }

        return localMarketId ?? string.Empty;
    }

    private static async Task TryLogAuditAsync(
        IOrderAuditLogger auditLogger,
        string clientOrderId,
        Guid orderId,
        string marketId,
        decimal originalQuantity,
        decimal filledQuantity,
        decimal? averagePrice,
        ExecutionStatus newStatus,
        OrderStateUpdate? previousState,
        OrderTrackingEntry? trackingEntry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditLogger);

        var prevFilled = previousState?.FilledQuantity ?? 0m;
        var prevStatus = previousState?.Status;

        var strategyId = trackingEntry?.StrategyId ?? string.Empty;
        var correlationId = trackingEntry?.CorrelationId;

        var fillDelta = filledQuantity - prevFilled;
        var hasFillIncrease = fillDelta > 0m;
        var becameFilled = newStatus == ExecutionStatus.Filled && prevStatus != ExecutionStatus.Filled && hasFillIncrease;

        if (hasFillIncrease || becameFilled)
        {
            var fillPrice = averagePrice ?? 0m;
            var isPartial = originalQuantity > 0m && filledQuantity < originalQuantity;

            TradingMetrics.RecordOrderFilled(strategyId, "live", isPartial);

            await auditLogger.LogOrderFilledAsync(
                    orderId,
                    clientOrderId,
                    strategyId,
                    marketId,
                    fillDelta,
                    fillPrice,
                    isPartial,
                    correlationId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        // 终态事件（对账得到的最终状态）
        if (prevStatus != newStatus)
        {
            if (newStatus == ExecutionStatus.Cancelled)
            {
                TradingMetrics.RecordOrderCancelled(strategyId, "live");
                await auditLogger.LogOrderCancelledAsync(
                        orderId,
                        clientOrderId,
                        strategyId,
                        marketId,
                        correlationId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (newStatus == ExecutionStatus.Expired)
            {
                TradingMetrics.RecordOrderExpired(strategyId, "live");
                await auditLogger.LogOrderExpiredAsync(
                        orderId,
                        clientOrderId,
                        strategyId,
                        marketId,
                        correlationId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task TryLogTradesForOrderAsync(
        IOrderAuditLogger auditLogger,
        IOrderRepository orderRepository,
        TradingAccountContext accountContext,
        string clientOrderId,
        string exchangeOrderId,
        IReadOnlyList<string>? associateTradeIds,
        string? marketId,
        OrderTrackingEntry? trackingEntry,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(exchangeOrderId))
        {
            return;
        }

        var persistedOrder = await ResolvePersistedOrderAsync(
                orderRepository,
                clientOrderId,
                exchangeOrderId,
                cancellationToken)
            .ConfigureAwait(false);

        var effectiveMarketId = ResolveAuditMarketId(marketId, persistedOrder?.MarketId, trackingEntry?.MarketId);
        if (string.IsNullOrWhiteSpace(effectiveMarketId))
        {
            return;
        }

        var result = await _clobClient.GetTradesAsync(effectiveMarketId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            _logger.LogDebug("获取成交列表失败，跳过成交落库: Market={Market}, Error={Error}",
                effectiveMarketId,
                result.Error?.Message);
            return;
        }

        var associateSet = associateTradeIds is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : associateTradeIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.Ordinal);

        foreach (var trade in result.Data)
        {
            var matchKind = ResolveTradeOrderMatchKind(trade, exchangeOrderId, associateSet);
            if (matchKind is null)
            {
                continue;
            }

            if (!TryMapTrade(trade, persistedOrder, matchKind.Value, out var side, out var outcome, out var price, out var quantity, out var fee))
            {
                _logger.LogWarning(
                    "成交字段解析失败，跳过成交落库: TradeId={TradeId}, OrderId={OrderId}",
                    trade.Id,
                    exchangeOrderId);
                continue;
            }

            await auditLogger.LogTradeAsync(
                    OrderAuditIds.ForClientOrderId(clientOrderId),
                    accountContext.TradingAccountId,
                    clientOrderId,
                    trackingEntry?.StrategyId ?? persistedOrder?.StrategyId ?? string.Empty,
                    effectiveMarketId,
                    string.IsNullOrWhiteSpace(trade.AssetId) ? persistedOrder?.TokenId ?? string.Empty : trade.AssetId,
                    outcome,
                    side,
                    price,
                    quantity,
                    string.IsNullOrWhiteSpace(trade.Id) ? $"{exchangeOrderId}:{price}:{quantity}" : trade.Id,
                    fee,
                    trackingEntry?.CorrelationId ?? persistedOrder?.CorrelationId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private enum TradeOrderMatchKind
    {
        DirectTakerOrder,
        AssociatedTrade
    }

    private static TradeOrderMatchKind? ResolveTradeOrderMatchKind(
        TradeInfo trade,
        string exchangeOrderId,
        HashSet<string> associateTradeIds)
    {
        if (string.Equals(trade.TakerOrderId, exchangeOrderId, StringComparison.Ordinal))
        {
            return TradeOrderMatchKind.DirectTakerOrder;
        }

        return !string.IsNullOrWhiteSpace(trade.Id) && associateTradeIds.Contains(trade.Id)
            ? TradeOrderMatchKind.AssociatedTrade
            : null;
    }

    private static bool TryMapTrade(
        TradeInfo trade,
        OrderDto? persistedOrder,
        TradeOrderMatchKind matchKind,
        out OrderSide side,
        out OutcomeSide outcome,
        out decimal price,
        out decimal quantity,
        out decimal fee)
    {
        side = persistedOrder?.Side ?? OrderSide.Buy;
        outcome = persistedOrder?.Outcome ?? OutcomeSide.Yes;
        price = 0m;
        quantity = 0m;
        fee = 0m;

        if (matchKind == TradeOrderMatchKind.AssociatedTrade && persistedOrder is null)
        {
            return false;
        }

        if (matchKind == TradeOrderMatchKind.DirectTakerOrder &&
            !string.IsNullOrWhiteSpace(trade.Side) &&
            Enum.TryParse<OrderSide>(NormalizeSide(trade.Side), ignoreCase: true, out var parsedSide))
        {
            side = parsedSide;
        }

        if (!string.IsNullOrWhiteSpace(trade.Outcome) &&
            Enum.TryParse<OutcomeSide>(trade.Outcome, ignoreCase: true, out var parsedOutcome))
        {
            outcome = parsedOutcome;
        }
        else if (persistedOrder is null)
        {
            return false;
        }

        if (!TryParseDecimalInvariant(trade.Price, out price) ||
            !TryParseDecimalInvariant(trade.Size, out quantity) ||
            quantity <= 0m)
        {
            return false;
        }

        if (TryParseDecimalInvariant(trade.FeeRateBps, out var feeRateBps) && feeRateBps > 0m)
        {
            fee = price * quantity * feeRateBps / 10000m;
        }

        return true;
    }

    private static string NormalizeSide(string side) => side.Trim().ToUpperInvariant() switch
    {
        "BUY" => nameof(OrderSide.Buy),
        "SELL" => nameof(OrderSide.Sell),
        _ => side
    };

    private static async Task PersistReconciledOrderAsync(
        IOrderRepository orderRepository,
        OrderDto? persistedOrder,
        string clientOrderId,
        string exchangeOrderId,
        string? marketId,
        string? tokenId,
        decimal filledQuantity,
        ExecutionStatus status,
        CancellationToken cancellationToken)
    {
        persistedOrder ??= await ResolvePersistedOrderAsync(
            orderRepository,
            clientOrderId,
            exchangeOrderId,
            cancellationToken).ConfigureAwait(false);

        if (persistedOrder is null)
        {
            return;
        }

        var normalizedFilled = NormalizeFilledQuantity(
            Math.Max(filledQuantity, persistedOrder.FilledQuantity),
            persistedOrder.Quantity);

        await orderRepository.UpdateAsync(persistedOrder with
            {
                ExchangeOrderId = string.IsNullOrWhiteSpace(exchangeOrderId)
                    ? persistedOrder.ExchangeOrderId
                    : exchangeOrderId,
                MarketId = string.IsNullOrWhiteSpace(marketId) ? persistedOrder.MarketId : marketId!,
                TokenId = string.IsNullOrWhiteSpace(tokenId) ? persistedOrder.TokenId : tokenId,
                FilledQuantity = normalizedFilled,
                Status = ToOrderStatus(status),
                UpdatedAtUtc = DateTimeOffset.UtcNow
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static OrderStatus ToOrderStatus(ExecutionStatus status) => status switch
    {
        ExecutionStatus.Pending => OrderStatus.Pending,
        ExecutionStatus.Accepted => OrderStatus.Open,
        ExecutionStatus.PartiallyFilled => OrderStatus.PartiallyFilled,
        ExecutionStatus.Filled => OrderStatus.Filled,
        ExecutionStatus.Cancelled => OrderStatus.Cancelled,
        ExecutionStatus.Rejected => OrderStatus.Rejected,
        ExecutionStatus.Expired => OrderStatus.Expired,
        _ => OrderStatus.Pending
    };

    private static ExecutionStatus ToExecutionStatus(OrderStatus status) => status switch
    {
        OrderStatus.Pending => ExecutionStatus.Pending,
        OrderStatus.Open => ExecutionStatus.Accepted,
        OrderStatus.PartiallyFilled => ExecutionStatus.PartiallyFilled,
        OrderStatus.Filled => ExecutionStatus.Filled,
        OrderStatus.Cancelled => ExecutionStatus.Cancelled,
        OrderStatus.Rejected => ExecutionStatus.Rejected,
        OrderStatus.Expired => ExecutionStatus.Expired,
        _ => ExecutionStatus.Pending
    };

    private static async Task<OrderDto?> ResolvePersistedOrderAsync(
        IOrderRepository orderRepository,
        string clientOrderId,
        string exchangeOrderId,
        CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByExchangeOrderIdAsync(exchangeOrderId, cancellationToken)
            .ConfigureAwait(false);
        if (order is not null)
        {
            return order;
        }

        return await orderRepository.GetByClientOrderIdAsync(clientOrderId, cancellationToken)
            .ConfigureAwait(false);
    }
}
