using System.Diagnostics;
using Autotrade.Trading.Application.Audit;
using Autotrade.MarketData.Application.Contract.OrderBook;
using Autotrade.Trading.Application.Contract.Audit;
using Autotrade.Trading.Application.Contract.Compliance;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Application.Metrics;
using Autotrade.Trading.Application.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Trading.Application.Execution;

/// <summary>
/// 模拟执行服务：基于订单簿数据模拟订单撮合。
/// </summary>
public sealed class PaperExecutionService : IExecutionService
{
    private readonly IOrderBookReader _orderBookReader;
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly IOrderStateTracker _stateTracker;
    private readonly OrderLimitValidator _limitValidator;
    private readonly IComplianceGuard _complianceGuard;
    private readonly IOrderAuditLogger _auditLogger;
    private readonly IRiskEventRepository _riskEventRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly PaperOrderStore _orderStore;
    private readonly TradingAccountContext _accountContext;
    private readonly PaperTradingOptions _paperOptions;
    private readonly ExecutionOptions _executionOptions;
    private readonly ILogger<PaperExecutionService> _logger;
    private readonly Random _random;

    public PaperExecutionService(
        IOrderBookReader orderBookReader,
        IIdempotencyStore idempotencyStore,
        IOrderStateTracker stateTracker,
        OrderLimitValidator limitValidator,
        IComplianceGuard complianceGuard,
        IOrderAuditLogger auditLogger,
        IRiskEventRepository riskEventRepository,
        IOrderRepository orderRepository,
        PaperOrderStore orderStore,
        TradingAccountContext accountContext,
        IOptions<PaperTradingOptions> paperOptions,
        IOptions<ExecutionOptions> executionOptions,
        ILogger<PaperExecutionService> logger)
    {
        _orderBookReader = orderBookReader ?? throw new ArgumentNullException(nameof(orderBookReader));
        _idempotencyStore = idempotencyStore ?? throw new ArgumentNullException(nameof(idempotencyStore));
        _stateTracker = stateTracker ?? throw new ArgumentNullException(nameof(stateTracker));
        _limitValidator = limitValidator ?? throw new ArgumentNullException(nameof(limitValidator));
        _complianceGuard = complianceGuard ?? throw new ArgumentNullException(nameof(complianceGuard));
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
        _riskEventRepository = riskEventRepository ?? throw new ArgumentNullException(nameof(riskEventRepository));
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _orderStore = orderStore ?? throw new ArgumentNullException(nameof(orderStore));
        _accountContext = accountContext ?? throw new ArgumentNullException(nameof(accountContext));
        _paperOptions = paperOptions?.Value ?? throw new ArgumentNullException(nameof(paperOptions));
        _executionOptions = executionOptions?.Value ?? throw new ArgumentNullException(nameof(executionOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _random = _paperOptions.DeterministicSeed.HasValue
            ? new Random(_paperOptions.DeterministicSeed.Value)
            : new Random();
    }

    /// <inheritdoc />
    public async Task<ExecutionResult> PlaceOrderAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. 验证请求
        var validationError = request.Validate();
        if (validationError is not null)
        {
            _logger.LogWarning("[Paper] 订单请求验证失败: {Error}, ClientOrderId={ClientOrderId}",
                validationError, request.ClientOrderId);

            return ExecutionResult.Fail(
                request.ClientOrderId,
                "VALIDATION_ERROR",
                validationError);
        }

        // 2. 幂等性检查（必须在限单检查之前）
        var requestHash = ExecutionRequestHasher.Compute(request);
        var ttl = TimeSpan.FromSeconds(_executionOptions.IdempotencyTtlSeconds);
        var isNewRequest = false;

        try
        {
            var (isNew, existingExchangeOrderId) = await _idempotencyStore
                .TryAddAsync(request.ClientOrderId, requestHash, ttl, cancellationToken)
                .ConfigureAwait(false);
            isNewRequest = isNew;

            if (!isNew)
            {
                if (existingExchangeOrderId is not null)
                {
                    _logger.LogInformation(
                        "[Paper] 重复订单请求: ClientOrderId={ClientOrderId}, ExchangeOrderId={ExchangeOrderId}",
                        request.ClientOrderId, existingExchangeOrderId);

                    return ExecutionResult.Succeed(
                        request.ClientOrderId,
                        existingExchangeOrderId,
                        ExecutionStatus.Accepted);
                }

                // 订单正在提交中
                _logger.LogInformation(
                    "[Paper] 订单正在提交中: ClientOrderId={ClientOrderId}",
                    request.ClientOrderId);

                return ExecutionResult.Succeed(
                    request.ClientOrderId,
                    string.Empty,
                    ExecutionStatus.Pending);
            }

            await _idempotencyStore
                .SetAuditInfoAsync(request.ClientOrderId, request.StrategyId, request.CorrelationId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (IdempotencyConflictException ex)
        {
            return ExecutionResult.Fail(
                request.ClientOrderId,
                "IDEMPOTENCY_CONFLICT",
                ex.Message);
        }

        // 3. 检查订单限制
        if (isNewRequest)
        {
            await LogPaperComplianceWarningsAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var limitError = _limitValidator.ValidateCanPlaceOrder(request.MarketId);
        if (limitError is not null)
        {
            _logger.LogWarning("[Paper] 订单限制检查失败: {Error}, ClientOrderId={ClientOrderId}",
                limitError, request.ClientOrderId);

            TradingMetrics.RecordOrderRejected(request.StrategyId, "paper", "order_limit_exceeded");

            // 清理幂等条目
            await _idempotencyStore
                .RemoveAsync(request.ClientOrderId, cancellationToken)
                .ConfigureAwait(false);

            return ExecutionResult.Fail(
                request.ClientOrderId,
                "ORDER_LIMIT_EXCEEDED",
                limitError);
        }

        TradingMetrics.RecordOrderSubmitted(request.StrategyId, "paper");
        var placeSw = Stopwatch.StartNew();

        // 3. 模拟延迟
        if (_paperOptions.SimulatedLatencyMs > 0)
        {
            await Task.Delay(_paperOptions.SimulatedLatencyMs, cancellationToken).ConfigureAwait(false);
        }

        // 4. 生成模拟交易所订单 ID
        var exchangeOrderId = _orderStore.GenerateExchangeOrderId();

        // 4.1 获取稳定的 TradingAccountId（Orders/Trades FK 需要）
        // ExecutionRequest 不携带 TradingAccountId（属于 Trading 内部持久化身份），由 Trading 侧统一解析/创建。
        var tradingAccountId = _accountContext.TradingAccountId;

        // 5. 创建模拟订单
        var paperOrder = new PaperOrder
        {
            ClientOrderId = request.ClientOrderId,
            ExchangeOrderId = exchangeOrderId,
            TradingAccountId = tradingAccountId,
            TokenId = request.TokenId,
            MarketId = request.MarketId,
            StrategyId = request.StrategyId,
            CorrelationId = request.CorrelationId,
            Outcome = request.Outcome,
            Side = request.Side,
            OrderType = request.OrderType,
            TimeInForce = request.TimeInForce,
            NegRisk = request.NegRisk,
            Price = request.Price,
            OriginalQuantity = request.Quantity,
            FilledQuantity = 0m,
            Status = ExecutionStatus.Accepted,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            GoodTilDateUtc = request.GoodTilDateUtc
        };

        _orderStore.AddOrUpdate(request.ClientOrderId, paperOrder);

        await _idempotencyStore
            .SetExchangeOrderIdAsync(request.ClientOrderId, exchangeOrderId, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "[Paper] 订单已接受: ClientOrderId={ClientOrderId}, ExchangeOrderId={ExchangeOrderId}, " +
            "Side={Side}, Price={Price}, Quantity={Quantity}",
            request.ClientOrderId, exchangeOrderId, request.Side, request.Price, request.Quantity);

        // 6. 上报状态跟踪器（订单接受）
        await _stateTracker.OnOrderStateChangedAsync(new OrderStateUpdate
        {
            ClientOrderId = request.ClientOrderId,
            ExchangeOrderId = exchangeOrderId,
            MarketId = request.MarketId,
            TokenId = request.TokenId,
            Status = ExecutionStatus.Accepted,
            OriginalQuantity = request.Quantity,
            FilledQuantity = 0m
        }, cancellationToken).ConfigureAwait(false);

        // 6.0 持久化 Orders（用于 CLI status/export orders）
        var orderId = OrderAuditIds.ForClientOrderId(request.ClientOrderId);
        await UpsertOrderAsync(new OrderDto(
                Id: orderId,
                TradingAccountId: tradingAccountId,
                MarketId: request.MarketId,
                TokenId: request.TokenId,
                StrategyId: request.StrategyId,
                ClientOrderId: request.ClientOrderId,
                ExchangeOrderId: exchangeOrderId,
                CorrelationId: request.CorrelationId,
                Outcome: request.Outcome,
                Side: request.Side,
                OrderType: request.OrderType,
                TimeInForce: request.TimeInForce,
                GoodTilDateUtc: request.GoodTilDateUtc,
                NegRisk: request.NegRisk,
                Price: request.Price,
                Quantity: request.Quantity,
                FilledQuantity: 0m,
                Status: OrderStatus.Open,
                RejectionReason: null,
                CreatedAtUtc: paperOrder.CreatedAtUtc,
                UpdatedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);

        // 6.1 审计日志 - 记录订单接受事件
        await _auditLogger.LogOrderAcceptedAsync(
            orderId,
            request.ClientOrderId,
            request.StrategyId ?? string.Empty,
            request.MarketId,
            exchangeOrderId,
            request.CorrelationId,
            cancellationToken).ConfigureAwait(false);

        placeSw.Stop();
        TradingMetrics.RecordPlaceOrderLatency(placeSw.Elapsed.TotalMilliseconds, request.StrategyId, "paper", success: true);
        TradingMetrics.RecordOrderAccepted(request.StrategyId, "paper");

        // 7. 模拟撮合（根据订单簿或默认规则）
        await SimulateFillAsync(paperOrder, orderId, request.StrategyId, request.CorrelationId, tradingAccountId, cancellationToken).ConfigureAwait(false);

        return new ExecutionResult
        {
            Success = true,
            ClientOrderId = request.ClientOrderId,
            ExchangeOrderId = exchangeOrderId,
            Status = paperOrder.Status,
            FilledQuantity = paperOrder.FilledQuantity,
            AveragePrice = paperOrder.FilledQuantity > 0 ? paperOrder.AverageFilledPrice : null
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExecutionResult>> PlaceOrdersAsync(
        IReadOnlyList<ExecutionRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var results = new List<ExecutionResult>(requests.Count);
        foreach (var request in requests)
        {
            results.Add(await PlaceOrderAsync(request, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<ExecutionResult> CancelOrderAsync(
        string clientOrderId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientOrderId))
        {
            return ExecutionResult.Fail(
                clientOrderId ?? string.Empty,
                "VALIDATION_ERROR",
                "ClientOrderId 不能为空");
        }

        if (!_orderStore.TryGet(clientOrderId, out var order) || order is null)
        {
            return ExecutionResult.Fail(
                clientOrderId,
                "ORDER_NOT_FOUND",
                "订单未找到");
        }

        // 检查订单是否可以取消
        if (order.Status is ExecutionStatus.Filled or ExecutionStatus.Cancelled or ExecutionStatus.Expired)
        {
            return ExecutionResult.Fail(
                clientOrderId,
                "INVALID_STATE",
                $"订单状态 {order.Status} 不允许取消");
        }

        order.Status = ExecutionStatus.Cancelled;
        order.UpdatedAtUtc = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "[Paper] 订单已取消: ClientOrderId={ClientOrderId}, ExchangeOrderId={ExchangeOrderId}",
            clientOrderId, order.ExchangeOrderId);

        TradingMetrics.RecordOrderCancelled(order.StrategyId, "paper");

        await _stateTracker.OnOrderStateChangedAsync(new OrderStateUpdate
        {
            ClientOrderId = order.ClientOrderId,
            ExchangeOrderId = order.ExchangeOrderId,
            MarketId = order.MarketId,
            TokenId = order.TokenId,
            Status = ExecutionStatus.Cancelled,
            OriginalQuantity = order.OriginalQuantity,
            FilledQuantity = order.FilledQuantity,
            AveragePrice = order.AverageFilledPrice
        }, cancellationToken).ConfigureAwait(false);

        // 审计日志 - 记录取消事件
        var orderId = OrderAuditIds.ForClientOrderId(order.ClientOrderId);
        await _auditLogger.LogOrderCancelledAsync(
            orderId,
            order.ClientOrderId,
            order.StrategyId ?? string.Empty,
            order.MarketId,
            order.CorrelationId,
            cancellationToken).ConfigureAwait(false);

        // 持久化 Orders
        await UpsertOrderAsync(new OrderDto(
                Id: orderId,
                TradingAccountId: order.TradingAccountId,
                MarketId: order.MarketId,
                TokenId: order.TokenId,
                StrategyId: order.StrategyId,
                ClientOrderId: order.ClientOrderId,
                ExchangeOrderId: order.ExchangeOrderId,
                CorrelationId: order.CorrelationId,
                Outcome: order.Outcome,
                Side: order.Side,
                OrderType: order.OrderType,
                TimeInForce: order.TimeInForce,
                GoodTilDateUtc: order.GoodTilDateUtc,
                NegRisk: order.NegRisk,
                Price: order.Price,
                Quantity: order.OriginalQuantity,
                FilledQuantity: order.FilledQuantity,
                Status: OrderStatus.Cancelled,
                RejectionReason: null,
                CreatedAtUtc: order.CreatedAtUtc,
                UpdatedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);

        return ExecutionResult.Succeed(
            clientOrderId,
            order.ExchangeOrderId,
            ExecutionStatus.Cancelled,
            order.FilledQuantity,
            order.AverageFilledPrice);
    }

    /// <inheritdoc />
    public Task<OrderStatusResult> GetOrderStatusAsync(
        string clientOrderId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientOrderId))
        {
            return Task.FromResult(OrderStatusResult.NotFound(clientOrderId ?? string.Empty));
        }

        if (!_orderStore.TryGet(clientOrderId, out var order) || order is null)
        {
            return Task.FromResult(OrderStatusResult.NotFound(clientOrderId));
        }

        return Task.FromResult(new OrderStatusResult
        {
            Found = true,
            ClientOrderId = clientOrderId,
            ExchangeOrderId = order.ExchangeOrderId,
            Status = order.Status,
            OriginalQuantity = order.OriginalQuantity,
            FilledQuantity = order.FilledQuantity,
            Price = order.Price,
            AverageFilledPrice = order.AverageFilledPrice,
            CreatedAtUtc = order.CreatedAtUtc,
            UpdatedAtUtc = order.UpdatedAtUtc
        });
    }

    private async Task SimulateFillAsync(
        PaperOrder order,
        Guid orderId,
        string? strategyId,
        string? correlationId,
        Guid tradingAccountId,
        CancellationToken cancellationToken)
    {
        // GTD 过期检查
        if (order.TimeInForce == TimeInForce.Gtd && order.GoodTilDateUtc.HasValue)
        {
            if (TimeInForceHandler.IsExpired(order.TimeInForce, order.GoodTilDateUtc))
            {
                order.Status = ExecutionStatus.Expired;
                order.UpdatedAtUtc = DateTimeOffset.UtcNow;
                _logger.LogDebug("[Paper] GTD 订单已过期: ClientOrderId={ClientOrderId}", order.ClientOrderId);

                TradingMetrics.RecordOrderExpired(strategyId, "paper");

                await _stateTracker.OnOrderStateChangedAsync(new OrderStateUpdate
                {
                    ClientOrderId = order.ClientOrderId,
                    ExchangeOrderId = order.ExchangeOrderId,
                    MarketId = order.MarketId,
                    TokenId = order.TokenId,
                    Status = ExecutionStatus.Expired,
                    OriginalQuantity = order.OriginalQuantity,
                    FilledQuantity = 0m
                }, cancellationToken).ConfigureAwait(false);

                await _auditLogger.LogOrderExpiredAsync(
                    orderId,
                    order.ClientOrderId,
                    strategyId ?? string.Empty,
                    order.MarketId,
                    correlationId,
                    cancellationToken).ConfigureAwait(false);

                await UpsertOrderAsync(new OrderDto(
                        Id: orderId,
                        TradingAccountId: order.TradingAccountId,
                        MarketId: order.MarketId,
                        TokenId: order.TokenId,
                        StrategyId: order.StrategyId,
                        ClientOrderId: order.ClientOrderId,
                        ExchangeOrderId: order.ExchangeOrderId,
                        CorrelationId: order.CorrelationId,
                        Outcome: order.Outcome,
                        Side: order.Side,
                        OrderType: order.OrderType,
                        TimeInForce: order.TimeInForce,
                        GoodTilDateUtc: order.GoodTilDateUtc,
                        NegRisk: order.NegRisk,
                        Price: order.Price,
                        Quantity: order.OriginalQuantity,
                        FilledQuantity: order.FilledQuantity,
                        Status: OrderStatus.Expired,
                        RejectionReason: null,
                        CreatedAtUtc: order.CreatedAtUtc,
                        UpdatedAtUtc: DateTimeOffset.UtcNow),
                    cancellationToken).ConfigureAwait(false);

                return;
            }
        }

        // 获取订单簿数据
        var topOfBook = _orderBookReader.GetTopOfBook(order.TokenId);

        decimal fillPrice;
        bool canFill;

        if (topOfBook is not null)
        {
            // 基于订单簿模拟
            if (order.Side == OrderSide.Buy)
            {
                // 买单：检查是否能以 ask 价成交
                var askPrice = topOfBook.BestAskPrice?.Value;
                canFill = askPrice.HasValue && order.Price >= askPrice.Value;
                fillPrice = askPrice ?? order.Price;
            }
            else
            {
                // 卖单：检查是否能以 bid 价成交
                var bidPrice = topOfBook.BestBidPrice?.Value;
                canFill = bidPrice.HasValue && order.Price <= bidPrice.Value;
                fillPrice = bidPrice ?? order.Price;
            }
        }
        else
        {
            // 无订单簿数据，使用默认成交率
            canFill = _random.NextDouble() < _paperOptions.DefaultFillRate;
            fillPrice = order.Price;
        }

        if (!canFill)
        {
            // FAK/FOK 价格不匹配时立即取消
            if (order.TimeInForce is TimeInForce.Fak or TimeInForce.Fok)
            {
                order.Status = ExecutionStatus.Cancelled;
                order.UpdatedAtUtc = DateTimeOffset.UtcNow;
                _logger.LogDebug("[Paper] {TIF} 订单价格不匹配，已取消: ClientOrderId={ClientOrderId}",
                    order.TimeInForce, order.ClientOrderId);

                TradingMetrics.RecordOrderCancelled(strategyId, "paper");

                // 上报取消状态
                await _stateTracker.OnOrderStateChangedAsync(new OrderStateUpdate
                {
                    ClientOrderId = order.ClientOrderId,
                    ExchangeOrderId = order.ExchangeOrderId,
                    MarketId = order.MarketId,
                    TokenId = order.TokenId,
                    Status = ExecutionStatus.Cancelled,
                    OriginalQuantity = order.OriginalQuantity,
                    FilledQuantity = 0m
                }, cancellationToken).ConfigureAwait(false);

                await _auditLogger.LogOrderCancelledAsync(
                    orderId,
                    order.ClientOrderId,
                    strategyId ?? string.Empty,
                    order.MarketId,
                    correlationId,
                    cancellationToken).ConfigureAwait(false);

                await UpsertOrderAsync(new OrderDto(
                        Id: orderId,
                        TradingAccountId: order.TradingAccountId,
                        MarketId: order.MarketId,
                        TokenId: order.TokenId,
                        StrategyId: order.StrategyId,
                        ClientOrderId: order.ClientOrderId,
                        ExchangeOrderId: order.ExchangeOrderId,
                        CorrelationId: order.CorrelationId,
                        Outcome: order.Outcome,
                        Side: order.Side,
                        OrderType: order.OrderType,
                        TimeInForce: order.TimeInForce,
                        GoodTilDateUtc: order.GoodTilDateUtc,
                        NegRisk: order.NegRisk,
                        Price: order.Price,
                        Quantity: order.OriginalQuantity,
                        FilledQuantity: order.FilledQuantity,
                        Status: OrderStatus.Cancelled,
                        RejectionReason: null,
                        CreatedAtUtc: order.CreatedAtUtc,
                        UpdatedAtUtc: DateTimeOffset.UtcNow),
                    cancellationToken).ConfigureAwait(false);

                return;
            }

            // GTC/GTD 订单挂起，等待后续成交
            _logger.LogDebug("[Paper] 订单未成交（价格不匹配）: ClientOrderId={ClientOrderId}", order.ClientOrderId);
            return;
        }

        // 应用滑点
        var slippage = _paperOptions.SlippageBps / 10000m;
        if (order.Side == OrderSide.Buy)
        {
            fillPrice *= (1m + slippage);
        }
        else
        {
            fillPrice *= (1m - slippage);
        }

        // 确保价格在有效范围内
        fillPrice = Math.Clamp(fillPrice, 0.01m, 0.99m);

        // 决定成交数量
        decimal fillQuantity;

        // FAK/FOK 处理
        if (order.TimeInForce == TimeInForce.Fok)
        {
            // FOK：必须全部成交，否则全部取消
            if (canFill)
            {
                fillQuantity = order.OriginalQuantity;
            }
            else
            {
                order.Status = ExecutionStatus.Cancelled;
                order.UpdatedAtUtc = DateTimeOffset.UtcNow;
                _logger.LogDebug("[Paper] FOK 订单无法全部成交，已取消: ClientOrderId={ClientOrderId}", order.ClientOrderId);
                await _stateTracker.OnOrderStateChangedAsync(new OrderStateUpdate
                {
                    ClientOrderId = order.ClientOrderId,
                    ExchangeOrderId = order.ExchangeOrderId,
                    MarketId = order.MarketId,
                    TokenId = order.TokenId,
                    Status = ExecutionStatus.Cancelled,
                    OriginalQuantity = order.OriginalQuantity,
                    FilledQuantity = 0m
                }, cancellationToken).ConfigureAwait(false);

                await _auditLogger.LogOrderCancelledAsync(
                    orderId,
                    order.ClientOrderId,
                    strategyId ?? string.Empty,
                    order.MarketId,
                    correlationId,
                    cancellationToken).ConfigureAwait(false);

                return;
            }
        }
        else if (order.TimeInForce == TimeInForce.Fak)
        {
            // FAK：允许部分成交，剩余取消
            if (_random.NextDouble() < _paperOptions.PartialFillProbability)
            {
                var ratio = _paperOptions.MinPartialFillRatio +
                            _random.NextDouble() * (_paperOptions.MaxPartialFillRatio - _paperOptions.MinPartialFillRatio);
                fillQuantity = order.OriginalQuantity * (decimal)ratio;
            }
            else
            {
                fillQuantity = order.OriginalQuantity;
            }
        }
        else
        {
            // GTC/GTD：标准成交逻辑
            if (_random.NextDouble() < _paperOptions.PartialFillProbability)
            {
                var ratio = _paperOptions.MinPartialFillRatio +
                            _random.NextDouble() * (_paperOptions.MaxPartialFillRatio - _paperOptions.MinPartialFillRatio);
                fillQuantity = order.OriginalQuantity * (decimal)ratio;
            }
            else
            {
                fillQuantity = order.OriginalQuantity;
            }
        }

        // 应用成交
        order.FilledQuantity = fillQuantity;
        order.AverageFilledPrice = fillPrice;
        order.UpdatedAtUtc = DateTimeOffset.UtcNow;

        if (fillQuantity >= order.OriginalQuantity)
        {
            order.Status = ExecutionStatus.Filled;
        }
        else if (fillQuantity > 0)
        {
            order.Status = order.TimeInForce == TimeInForce.Fak
                ? ExecutionStatus.Cancelled // FAK 部分成交后取消剩余
                : ExecutionStatus.PartiallyFilled;
        }

        _logger.LogInformation(
            "[Paper] 订单成交: ClientOrderId={ClientOrderId}, FilledQty={FilledQty}, FillPrice={FillPrice}, Status={Status}",
            order.ClientOrderId, fillQuantity, fillPrice, order.Status);

        // 上报成交/部分成交/取消状态
        await _stateTracker.OnOrderStateChangedAsync(new OrderStateUpdate
        {
            ClientOrderId = order.ClientOrderId,
            ExchangeOrderId = order.ExchangeOrderId,
            MarketId = order.MarketId,
            TokenId = order.TokenId,
            Status = order.Status,
            OriginalQuantity = order.OriginalQuantity,
            FilledQuantity = order.FilledQuantity,
            AveragePrice = order.AverageFilledPrice
        }, cancellationToken).ConfigureAwait(false);

        // 审计日志 - 记录成交事件
        var isPartial = fillQuantity < order.OriginalQuantity;

        TradingMetrics.RecordOrderFilled(strategyId, "paper", isPartial);
        TradingMetrics.RecordFillLatency(
            (order.UpdatedAtUtc!.Value - order.CreatedAtUtc).TotalMilliseconds,
            strategyId,
            "paper",
            isPartial);

        await _auditLogger.LogOrderFilledAsync(
            orderId,
            order.ClientOrderId,
            strategyId ?? string.Empty,
            order.MarketId,
            fillQuantity,
            fillPrice,
            isPartial,
            correlationId,
            cancellationToken).ConfigureAwait(false);

        if (order.Status == ExecutionStatus.Cancelled)
        {
            TradingMetrics.RecordOrderCancelled(strategyId, "paper");
            await _auditLogger.LogOrderCancelledAsync(
                orderId,
                order.ClientOrderId,
                strategyId ?? string.Empty,
                order.MarketId,
                correlationId,
                cancellationToken).ConfigureAwait(false);
        }

        // 审计日志 - 记录成交（Trade）
        if (fillQuantity > 0)
        {
            TradingMetrics.RecordTradeExecuted(strategyId, "paper", order.Side.ToString());

            await _auditLogger.LogTradeAsync(
                orderId,
                tradingAccountId,
                order.ClientOrderId,
                strategyId ?? string.Empty,
                order.MarketId,
                order.TokenId,
                order.Outcome,
                order.Side,
                fillPrice,
                fillQuantity,
                $"PAPER-TRADE-{Guid.NewGuid():N}",
                0m, // Paper trading 无手续费
                correlationId,
                cancellationToken).ConfigureAwait(false);
        }

        // 持久化 Orders（状态/成交数量）
        await UpsertOrderAsync(new OrderDto(
                Id: orderId,
                TradingAccountId: order.TradingAccountId,
                MarketId: order.MarketId,
                TokenId: order.TokenId,
                StrategyId: order.StrategyId,
                ClientOrderId: order.ClientOrderId,
                ExchangeOrderId: order.ExchangeOrderId,
                CorrelationId: order.CorrelationId,
                Outcome: order.Outcome,
                Side: order.Side,
                OrderType: order.OrderType,
                TimeInForce: order.TimeInForce,
                GoodTilDateUtc: order.GoodTilDateUtc,
                NegRisk: order.NegRisk,
                Price: order.Price,
                Quantity: order.OriginalQuantity,
                FilledQuantity: order.FilledQuantity,
                Status: ToOrderStatus(order.Status),
                RejectionReason: null,
                CreatedAtUtc: order.CreatedAtUtc,
                UpdatedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task LogPaperComplianceWarningsAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var check = _complianceGuard.CheckOrderPlacement(_executionOptions.Mode);
        foreach (var issue in check.Issues)
        {
            _logger.LogWarning(
                "[Paper] Compliance warning: {Code} {Message}",
                issue.Code,
                issue.Message);

            await _riskEventRepository.AddAsync(
                    issue.Code,
                    RiskSeverity.Warning,
                    $"Paper compliance warning for {request.ClientOrderId}: {issue.Message}",
                    request.StrategyId ?? "unknown",
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
        }
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

    private async Task UpsertOrderAsync(OrderDto order, CancellationToken cancellationToken)
    {
        // 订单持久化必须成功：否则 CLI 查询/导出/回放将失真，且无法满足审计与可观测性要求。
        var existing = await _orderRepository.GetByIdAsync(order.Id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            await _orderRepository.AddAsync(order, cancellationToken).ConfigureAwait(false);
            return;
        }

        await _orderRepository.UpdateAsync(order, cancellationToken).ConfigureAwait(false);
    }
}
