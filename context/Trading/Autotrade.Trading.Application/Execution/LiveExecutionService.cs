using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Autotrade.Polymarket.Abstractions;
using Autotrade.Polymarket.Models;
using Autotrade.Trading.Application.Audit;
using Autotrade.Trading.Application.Metrics;
using Autotrade.Trading.Application.Risk;
using Autotrade.Trading.Application.Contract.Audit;
using Autotrade.Trading.Application.Contract.Compliance;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Trading.Application.Execution;

/// <summary>
/// 实盘执行服务：通过 Polymarket API 执行真实交易。
/// </summary>
public sealed class LiveExecutionService : IExecutionService
{
    private readonly IPolymarketClobClient _clobClient;
    private readonly IPolymarketOrderSigner _orderSigner;
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly IOrderStateTracker _stateTracker;
    private readonly OrderLimitValidator _limitValidator;
    private readonly IComplianceGuard _complianceGuard;
    private readonly ILiveArmingService _liveArmingService;
    private readonly IOrderAuditLogger _auditLogger;
    private readonly IRiskManager _riskManager;
    private readonly IRiskEventRepository _riskEventRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly TradingAccountContext _accountContext;
    private readonly ExecutionOptions _options;
    private readonly ILogger<LiveExecutionService> _logger;

    public LiveExecutionService(
        IPolymarketClobClient clobClient,
        IPolymarketOrderSigner orderSigner,
        IIdempotencyStore idempotencyStore,
        IOrderStateTracker stateTracker,
        OrderLimitValidator limitValidator,
        IComplianceGuard complianceGuard,
        ILiveArmingService liveArmingService,
        IOrderAuditLogger auditLogger,
        IRiskManager riskManager,
        IRiskEventRepository riskEventRepository,
        IOrderRepository orderRepository,
        TradingAccountContext accountContext,
        IOptions<ExecutionOptions> options,
        ILogger<LiveExecutionService> logger)
    {
        _clobClient = clobClient ?? throw new ArgumentNullException(nameof(clobClient));
        _orderSigner = orderSigner ?? throw new ArgumentNullException(nameof(orderSigner));
        _idempotencyStore = idempotencyStore ?? throw new ArgumentNullException(nameof(idempotencyStore));
        _stateTracker = stateTracker ?? throw new ArgumentNullException(nameof(stateTracker));
        _limitValidator = limitValidator ?? throw new ArgumentNullException(nameof(limitValidator));
        _complianceGuard = complianceGuard ?? throw new ArgumentNullException(nameof(complianceGuard));
        _liveArmingService = liveArmingService ?? throw new ArgumentNullException(nameof(liveArmingService));
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
        _riskManager = riskManager ?? throw new ArgumentNullException(nameof(riskManager));
        _riskEventRepository = riskEventRepository ?? throw new ArgumentNullException(nameof(riskEventRepository));
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _accountContext = accountContext ?? throw new ArgumentNullException(nameof(accountContext));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
            _logger.LogWarning("订单请求验证失败: {Error}, ClientOrderId={ClientOrderId}",
                validationError, request.ClientOrderId);

            return ExecutionResult.Fail(
                request.ClientOrderId,
                "VALIDATION_ERROR",
                validationError);
        }

        // 2. 幂等性检查（必须先于 compliance/限单，确保重复请求返回已有结果）
        var persistedResult = await TryReturnPersistedOrderResultAsync(request, cancellationToken).ConfigureAwait(false);
        if (persistedResult is not null)
        {
            return persistedResult;
        }

        var liveArmingBlock = await TryBlockForLiveArmingAsync(request, cancellationToken).ConfigureAwait(false);
        if (liveArmingBlock is not null)
        {
            return liveArmingBlock;
        }

        var requestHash = ExecutionRequestHasher.Compute(request);
        var ttl = TimeSpan.FromSeconds(_options.IdempotencyTtlSeconds);
        var isRetryingUncertainSubmit = false;
        var isNewRequest = false;

        try
        {
            var (isNew, existingExchangeOrderId) = await _idempotencyStore
                .TryAddAsync(request.ClientOrderId, requestHash, ttl, cancellationToken)
                .ConfigureAwait(false);
            isNewRequest = isNew;

            if (!isNew)
            {
                _logger.LogInformation(
                    "重复订单请求，返回已有结果: ClientOrderId={ClientOrderId}, ExchangeOrderId={ExchangeOrderId}",
                    request.ClientOrderId, existingExchangeOrderId);

                if (existingExchangeOrderId is not null)
                {
                    return ExecutionResult.Succeed(
                        request.ClientOrderId,
                        existingExchangeOrderId,
                        ExecutionStatus.Accepted);
                }

                if (await IsRetryableUncertainSubmitAsync(request.ClientOrderId, cancellationToken).ConfigureAwait(false))
                {
                    isRetryingUncertainSubmit = true;
                    _logger.LogInformation(
                        "重试不确定下单请求: ClientOrderId={ClientOrderId}",
                        request.ClientOrderId);
                }
                else
                {
                    // 订单正在提交中
                    return ExecutionResult.Succeed(
                        request.ClientOrderId,
                        string.Empty,
                        ExecutionStatus.Pending);
                }
            }

            if (isNew)
            {
                await _idempotencyStore
                    .SetAuditInfoAsync(request.ClientOrderId, request.StrategyId, request.CorrelationId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (IdempotencyConflictException ex)
        {
            _logger.LogWarning(ex, "幂等性冲突: ClientOrderId={ClientOrderId}", request.ClientOrderId);
            TradingMetrics.RecordOrderRejected(request.StrategyId, "live", "idempotency_conflict");
            return ExecutionResult.Fail(
                request.ClientOrderId,
                "IDEMPOTENCY_CONFLICT",
                ex.Message);
        }

        if (isNewRequest)
        {
            var complianceBlock = await TryBlockForComplianceAsync(request, cancellationToken).ConfigureAwait(false);
            if (complianceBlock is not null)
            {
                return complianceBlock;
            }
        }

        // 3. 检查订单限制（幂等检查通过后）
        if (!isRetryingUncertainSubmit)
        {
            var limitError = _limitValidator.ValidateCanPlaceOrder(request.MarketId);
            if (limitError is not null)
            {
                _logger.LogWarning("订单限制检查失败: {Error}, ClientOrderId={ClientOrderId}",
                    limitError, request.ClientOrderId);

                TradingMetrics.RecordOrderRejected(request.StrategyId, "live", "order_limit_exceeded");

                // 清理幂等条目以允许后续重试
                await _idempotencyStore
                    .RemoveAsync(request.ClientOrderId, cancellationToken)
                    .ConfigureAwait(false);

                return ExecutionResult.Fail(
                    request.ClientOrderId,
                    "ORDER_LIMIT_EXCEEDED",
                    limitError);
            }
        }

        // 存储市场信息用于撤单时状态更新
        await _idempotencyStore
            .SetMarketInfoAsync(request.ClientOrderId, request.MarketId, request.TokenId, cancellationToken)
            .ConfigureAwait(false);

        // 4. 构造 Polymarket 订单请求
        var signingPayload = isNewRequest
            ? await _idempotencyStore
                .GetOrCreateOrderSigningPayloadAsync(request.ClientOrderId, cancellationToken)
                .ConfigureAwait(false)
            : await GetRequiredSigningPayloadForRetryAsync(request, cancellationToken).ConfigureAwait(false);
        if (signingPayload is null)
        {
            return ExecutionResult.Succeed(
                request.ClientOrderId,
                string.Empty,
                ExecutionStatus.Pending);
        }

        var orderRequest = BuildOrderRequest(request, signingPayload);
        await PersistPreparedOrderPendingAsync(request, signingPayload, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "提交订单: ClientOrderId={ClientOrderId}, TokenId={TokenId}, Side={Side}, Price={Price}, Size={Size}",
            request.ClientOrderId,
            request.TokenId,
            request.Side,
            request.Price,
            request.Quantity);

        // 5. 调用 API 下单
        TradingMetrics.RecordOrderSubmitted(request.StrategyId, "live");
        var sw = Stopwatch.StartNew();
        PolymarketApiResult<OrderResponse> result;
        try
        {
            result = await _clobClient
                .PlaceOrderAsync(orderRequest, idempotencyKey: request.ClientOrderId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            TradingMetrics.RecordPlaceOrderLatency(sw.Elapsed.TotalMilliseconds, request.StrategyId, "live", success: false);
            _logger.LogWarning(
                ex,
                "Live order submit result is uncertain: ClientOrderId={ClientOrderId}",
                request.ClientOrderId);

            return await MarkPreparedOrderUncertainAsync(
                    request,
                    signingPayload,
                    "API_RESULT_UNCERTAIN",
                    ex.Message,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (sw.IsRunning)
            {
                sw.Stop();
            }
        }

        var successForLatency = result.IsSuccess && result.Data?.Success == true;
        TradingMetrics.RecordPlaceOrderLatency(sw.Elapsed.TotalMilliseconds, request.StrategyId, "live", successForLatency);

        if (!result.IsSuccess || result.Data is null)
        {
            var errorMsg = result.Error?.Message ?? "未知错误";
            if (!result.IsSuccess && IsDefinitiveOrderCreationRejectedStatus(result.StatusCode))
            {
                _logger.LogWarning(
                    "Live order submit was definitively rejected: ClientOrderId={ClientOrderId}, StatusCode={StatusCode}, Error={Error}",
                    request.ClientOrderId,
                    result.StatusCode,
                    errorMsg);

                return await RejectPreparedOrderAsync(
                        request,
                        signingPayload,
                        "ORDER_REJECTED",
                        errorMsg,
                        "api_rejected",
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            _logger.LogWarning(
                "Live order submit result is uncertain: ClientOrderId={ClientOrderId}, Error={Error}",
                request.ClientOrderId, errorMsg);

            return await MarkPreparedOrderUncertainAsync(
                    request,
                    signingPayload,
                    "API_RESULT_UNCERTAIN",
                    errorMsg,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var response = result.Data;

        if (!response.Success)
        {
            _logger.LogWarning(
                "下单被拒绝: ClientOrderId={ClientOrderId}, ErrorMsg={ErrorMsg}",
                request.ClientOrderId, response.ErrorMsg);

            TradingMetrics.RecordOrderRejected(request.StrategyId, "live", "order_rejected");

            return await RejectPreparedOrderAsync(
                    request,
                    signingPayload,
                    "ORDER_REJECTED",
                    response.ErrorMsg ?? "订单被拒绝",
                    metricsReason: null,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        // 6. 保存交易所订单 ID
        var exchangeOrderId = response.OrderId;
        if (string.IsNullOrWhiteSpace(exchangeOrderId))
        {
            const string error = "Exchange order id is missing from successful order response.";
            _logger.LogWarning(
                "下单响应缺少交易所订单 ID: ClientOrderId={ClientOrderId}",
                request.ClientOrderId);
            return await MarkPreparedOrderUncertainAsync(
                    request,
                    signingPayload,
                    "MISSING_EXCHANGE_ORDER_ID",
                    error,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await _idempotencyStore
            .SetExchangeOrderIdAsync(request.ClientOrderId, exchangeOrderId, cancellationToken)
            .ConfigureAwait(false);

        // 7. 更新订单状态跟踪
        var status = MapPlacementStatus(response.Status);
        await UpsertOrderAsync(
            CreateOrderDto(request, exchangeOrderId, 0m, status, signingPayload: signingPayload),
            cancellationToken).ConfigureAwait(false);

        await _stateTracker.OnOrderStateChangedAsync(new OrderStateUpdate
        {
            ClientOrderId = request.ClientOrderId,
            ExchangeOrderId = exchangeOrderId,
            MarketId = request.MarketId,
            TokenId = request.TokenId,
            Status = status,
            OriginalQuantity = request.Quantity,
            FilledQuantity = 0m
        }, cancellationToken).ConfigureAwait(false);

        // 8. 审计日志 - 记录订单接受事件
        var orderId = OrderAuditIds.ForClientOrderId(request.ClientOrderId);
        await _auditLogger.LogOrderAcceptedAsync(
            orderId,
            request.ClientOrderId,
            request.StrategyId ?? string.Empty,
            request.MarketId,
            exchangeOrderId,
            request.CorrelationId,
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "订单提交成功: ClientOrderId={ClientOrderId}, ExchangeOrderId={ExchangeOrderId}",
            request.ClientOrderId, exchangeOrderId);

        TradingMetrics.RecordOrderAccepted(request.StrategyId, "live");

        return ExecutionResult.Succeed(
            request.ClientOrderId,
            exchangeOrderId,
            status);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExecutionResult>> PlaceOrdersAsync(
        IReadOnlyList<ExecutionRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0)
        {
            return Array.Empty<ExecutionResult>();
        }

        if (!_options.UseBatchOrders || requests.Count == 1)
        {
            return await PlaceOrdersSequentiallyAsync(requests, cancellationToken).ConfigureAwait(false);
        }

        var results = new ExecutionResult?[requests.Count];
        var prepared = new List<PreparedBatchOrder>(requests.Count);
        var reservationsByMarket = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < requests.Count; i++)
        {
            var preparation = await TryPrepareBatchOrderAsync(
                    requests[i],
                    i,
                    reservationsByMarket,
                    cancellationToken)
                .ConfigureAwait(false);

            if (preparation.PreparedOrder is not null)
            {
                prepared.Add(preparation.PreparedOrder);
            }
            else
            {
                results[i] = preparation.Result
                    ?? throw new InvalidOperationException("Batch order preparation produced neither a prepared order nor a result.");
            }
        }

        if (prepared.Count == 0)
        {
            return results.Select(result => result!).ToArray();
        }

        var maxBatchSize = Math.Clamp(_options.MaxBatchOrderSize, 1, 15);
        foreach (var chunk in prepared.Chunk(maxBatchSize))
        {
            IReadOnlyList<PostOrderRequest> postOrders;
            try
            {
                postOrders = chunk
                    .Select(order => _orderSigner.CreatePostOrderRequest(
                        order.OrderRequest,
                        BuildOrderSigningKey(order.Request)))
                    .ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Unable to build signed CLOB batch envelopes. Falling back to sequential placement for {Count} orders.",
                    chunk.Length);

                await RemovePreparedBatchEntriesAsync(chunk, cancellationToken).ConfigureAwait(false);
                await FillSequentialFallbackResultsAsync(chunk, results, cancellationToken).ConfigureAwait(false);
                continue;
            }

            foreach (var preparedOrder in chunk)
            {
                await PersistPreparedOrderPendingAsync(
                        preparedOrder.Request,
                        preparedOrder.SigningPayload,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var sw = Stopwatch.StartNew();
            PolymarketApiResult<IReadOnlyList<OrderResponse>> batchResult;
            try
            {
                batchResult = await _clobClient
                    .PlaceOrdersAsync(
                        postOrders,
                        BuildBatchIdempotencyKey(chunk),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                TradingMetrics.RecordPlaceOrderLatency(sw.Elapsed.TotalMilliseconds, "batch", "live", success: false);
                _logger.LogWarning(
                    ex,
                    "CLOB batch placement result is uncertain. Leaving {Count} prepared orders pending for reconciliation.",
                    chunk.Length);

                await MarkBatchOrdersUncertainAsync(
                        chunk,
                        results,
                        "API_RESULT_UNCERTAIN",
                        ex.Message,
                        cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }
            finally
            {
                if (sw.IsRunning)
                {
                    sw.Stop();
                }
            }

            var successForLatency = batchResult.IsSuccess &&
                batchResult.Data is not null &&
                batchResult.Data.Count == chunk.Length;
            TradingMetrics.RecordPlaceOrderLatency(sw.Elapsed.TotalMilliseconds, "batch", "live", successForLatency);

            if (!batchResult.IsSuccess || batchResult.Data is null || batchResult.Data.Count != chunk.Length)
            {
                if (!batchResult.IsSuccess && IsBatchEndpointUnavailableStatus(batchResult.StatusCode))
                {
                    _logger.LogWarning(
                        "CLOB batch placement endpoint is unavailable. Falling back to sequential placement for {Count} orders. StatusCode={StatusCode}, Error={Error}",
                        chunk.Length,
                        batchResult.StatusCode,
                        batchResult.Error?.Message);

                    await RemovePreparedBatchEntriesAsync(chunk, cancellationToken).ConfigureAwait(false);
                    await ReleasePreparedBatchOrderStatesAsync(chunk, cancellationToken).ConfigureAwait(false);
                    await FillSequentialFallbackResultsAsync(chunk, results, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!batchResult.IsSuccess && IsDefinitiveOrderCreationRejectedStatus(batchResult.StatusCode))
                {
                    _logger.LogWarning(
                        "CLOB batch placement was definitively rejected. Marking {Count} prepared orders rejected. StatusCode={StatusCode}, Error={Error}",
                        chunk.Length,
                        batchResult.StatusCode,
                        batchResult.Error?.Message);

                    await MarkBatchOrdersRejectedAsync(
                            chunk,
                            results,
                            "ORDER_REJECTED",
                            batchResult.Error?.Message ?? "Batch placement was rejected by the exchange.",
                            "api_rejected",
                            cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                _logger.LogWarning(
                    "CLOB batch placement failed or returned an unmappable response. Leaving {Count} prepared orders pending for reconciliation. StatusCode={StatusCode}, Error={Error}, ResponseCount={ResponseCount}",
                    chunk.Length,
                    batchResult.StatusCode,
                    batchResult.Error?.Message,
                    batchResult.Data?.Count);

                await MarkBatchOrdersUncertainAsync(
                        chunk,
                        results,
                        "API_RESULT_UNCERTAIN",
                        batchResult.Error?.Message ?? "Batch placement result is uncertain.",
                        cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            for (var i = 0; i < chunk.Length; i++)
            {
                results[chunk[i].Index] = await CompleteBatchOrderAsync(
                        chunk[i],
                        batchResult.Data[i],
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return results.Select(result => result!).ToArray();
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

        // 查找交易所订单 ID
        var entry = await ResolveActionableTrackingEntryAsync(clientOrderId, cancellationToken)
            .ConfigureAwait(false);

        if (entry?.ExchangeOrderId is null)
        {
            _logger.LogWarning("撤单失败，找不到交易所订单 ID: ClientOrderId={ClientOrderId}", clientOrderId);
            return ExecutionResult.Fail(
                clientOrderId,
                "ORDER_NOT_FOUND",
                "找不到对应的交易所订单 ID");
        }

        _logger.LogInformation(
            "撤销订单: ClientOrderId={ClientOrderId}, ExchangeOrderId={ExchangeOrderId}",
            clientOrderId, entry.ExchangeOrderId);

        var result = await _clobClient
            .CancelOrderAsync(entry.ExchangeOrderId, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            var errorMsg = result.Error?.Message ?? "未知错误";
            _logger.LogWarning(
                "撤单失败: ClientOrderId={ClientOrderId}, Error={Error}",
                clientOrderId, errorMsg);

            return ExecutionResult.Fail(
                clientOrderId,
                "API_ERROR",
                errorMsg);
        }

        var response = result.Data;
        var canceled = response?.Canceled?.Contains(entry.ExchangeOrderId) ?? false;

        if (canceled)
        {
            // 更新订单状态（包含 MarketId 以正确更新挂单计数）
            await _stateTracker.OnOrderStateChangedAsync(new OrderStateUpdate
            {
                ClientOrderId = clientOrderId,
                ExchangeOrderId = entry.ExchangeOrderId,
                MarketId = entry.MarketId,
                TokenId = entry.TokenId,
                Status = ExecutionStatus.Cancelled
            }, cancellationToken).ConfigureAwait(false);

            // 审计日志 - 记录订单取消事件
            await _auditLogger.LogOrderCancelledAsync(
                OrderAuditIds.ForClientOrderId(clientOrderId),
                clientOrderId,
                entry.StrategyId ?? string.Empty,
                entry.MarketId ?? string.Empty,
                entry.CorrelationId,
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "订单已撤销: ClientOrderId={ClientOrderId}, ExchangeOrderId={ExchangeOrderId}",
                clientOrderId, entry.ExchangeOrderId);

            TradingMetrics.RecordOrderCancelled(entry.StrategyId, "live");
            await MarkPersistedOrderStatusAsync(clientOrderId, ExecutionStatus.Cancelled, cancellationToken)
                .ConfigureAwait(false);

            return ExecutionResult.Succeed(
                clientOrderId,
                entry.ExchangeOrderId,
                ExecutionStatus.Cancelled);
        }

        var notCanceledReason = response?.NotCanceled?.GetValueOrDefault(entry.ExchangeOrderId);
        _logger.LogWarning(
            "订单撤销失败: ClientOrderId={ClientOrderId}, Reason={Reason}",
            clientOrderId, notCanceledReason);

        return ExecutionResult.Fail(
            clientOrderId,
            "CANCEL_FAILED",
            notCanceledReason ?? "撤单失败");
    }

    /// <inheritdoc />
    public async Task<OrderStatusResult> GetOrderStatusAsync(
        string clientOrderId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientOrderId))
        {
            return OrderStatusResult.NotFound(clientOrderId ?? string.Empty, "ClientOrderId 不能为空");
        }

        // 查找交易所订单 ID
        var entry = await ResolveActionableTrackingEntryAsync(clientOrderId, cancellationToken)
            .ConfigureAwait(false);

        if (entry?.ExchangeOrderId is null)
        {
            return OrderStatusResult.NotFound(clientOrderId, "找不到对应的交易所订单 ID");
        }

        var result = await _clobClient
            .GetOrderAsync(entry.ExchangeOrderId, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess || result.Data is null)
        {
            return OrderStatusResult.NotFound(clientOrderId, result.Error?.Message);
        }

        var order = result.Data;

        return new OrderStatusResult
        {
            Found = true,
            ClientOrderId = clientOrderId,
            ExchangeOrderId = entry.ExchangeOrderId,
            Status = MapOrderInfoStatus(order.Status),
            OriginalQuantity = decimal.TryParse(order.OriginalSize, out var origSize) ? origSize : 0m,
            FilledQuantity = decimal.TryParse(order.SizeMatched, out var filled) ? filled : 0m,
            Price = decimal.TryParse(order.Price, out var price) ? price : 0m,
            CreatedAtUtc = DateTimeOffset.TryParse(order.CreatedAt, out var created) ? created : null
        };
    }

    private static OrderRequest BuildOrderRequest(ExecutionRequest request, OrderSigningPayload? signingPayload = null)
    {
        return new OrderRequest
        {
            TokenId = request.TokenId,
            Price = request.Price.ToString("0.##"),
            Size = request.Quantity.ToString("0.######"),
            Side = request.Side == OrderSide.Buy ? "BUY" : "SELL",
            TimeInForce = MapTimeInForce(request.TimeInForce),
            Expiration = request.TimeInForce == TimeInForce.Gtd && request.GoodTilDateUtc.HasValue
                ? request.GoodTilDateUtc.Value.ToUnixTimeSeconds()
                : null,
            NegRisk = request.NegRisk ? true : null,
            Salt = signingPayload?.Salt,
            Timestamp = signingPayload?.Timestamp
        };
    }

    private async Task PersistPreparedOrderPendingAsync(
        ExecutionRequest request,
        OrderSigningPayload signingPayload,
        CancellationToken cancellationToken)
    {
        await UpsertOrderAsync(
            CreateOrderDto(request, null, 0m, ExecutionStatus.Pending, signingPayload: signingPayload),
            cancellationToken).ConfigureAwait(false);

        await _stateTracker.OnOrderStateChangedAsync(new OrderStateUpdate
        {
            ClientOrderId = request.ClientOrderId,
            ExchangeOrderId = string.Empty,
            MarketId = request.MarketId,
            TokenId = request.TokenId,
            Status = ExecutionStatus.Pending,
            OriginalQuantity = request.Quantity,
            FilledQuantity = 0m
        }, cancellationToken).ConfigureAwait(false);
    }

    private Task MarkPreparedOrderRejectedAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken)
    {
        return _stateTracker.OnOrderStateChangedAsync(new OrderStateUpdate
        {
            ClientOrderId = request.ClientOrderId,
            ExchangeOrderId = string.Empty,
            MarketId = request.MarketId,
            TokenId = request.TokenId,
            Status = ExecutionStatus.Rejected,
            OriginalQuantity = request.Quantity,
            FilledQuantity = 0m
        }, cancellationToken);
    }

    private async Task<ExecutionResult> MarkPreparedOrderUncertainAsync(
        ExecutionRequest request,
        OrderSigningPayload? signingPayload,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await _idempotencyStore
            .MarkSubmitUncertainAsync(request.ClientOrderId, cancellationToken)
            .ConfigureAwait(false);

        await UpsertOrderAsync(
            CreateOrderDto(request, null, 0m, ExecutionStatus.Pending, signingPayload: signingPayload),
            cancellationToken).ConfigureAwait(false);

        await _stateTracker.OnOrderStateChangedAsync(new OrderStateUpdate
        {
            ClientOrderId = request.ClientOrderId,
            ExchangeOrderId = string.Empty,
            MarketId = request.MarketId,
            TokenId = request.TokenId,
            Status = ExecutionStatus.Pending,
            OriginalQuantity = request.Quantity,
            FilledQuantity = 0m
        }, cancellationToken).ConfigureAwait(false);

        await _auditLogger.LogOrderSubmittedAsync(
            OrderAuditIds.ForClientOrderId(request.ClientOrderId),
            request.ClientOrderId,
            request.StrategyId ?? string.Empty,
            request.MarketId,
            request.CorrelationId,
            cancellationToken).ConfigureAwait(false);

        return ExecutionResult.Fail(
            request.ClientOrderId,
            errorCode,
            errorMessage,
            ExecutionStatus.Pending);
    }

    private async Task<ExecutionResult> RejectPreparedOrderAsync(
        ExecutionRequest request,
        OrderSigningPayload? signingPayload,
        string errorCode,
        string errorMessage,
        string? metricsReason,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(metricsReason))
        {
            TradingMetrics.RecordOrderRejected(request.StrategyId, "live", metricsReason);
        }

        await UpsertOrderAsync(
            CreateOrderDto(request, null, 0m, ExecutionStatus.Rejected, errorMessage, signingPayload),
            cancellationToken).ConfigureAwait(false);

        await _idempotencyStore
            .ClearSubmitUncertainAsync(request.ClientOrderId, cancellationToken)
            .ConfigureAwait(false);

        await MarkPreparedOrderRejectedAsync(request, cancellationToken).ConfigureAwait(false);

        await _auditLogger.LogOrderRejectedAsync(
            OrderAuditIds.ForClientOrderId(request.ClientOrderId),
            request.ClientOrderId,
            request.StrategyId ?? string.Empty,
            request.MarketId,
            errorCode,
            errorMessage,
            request.CorrelationId,
            cancellationToken).ConfigureAwait(false);

        await _riskManager.RecordOrderErrorAsync(
                request.StrategyId ?? "unknown",
                request.ClientOrderId,
                errorCode,
                errorMessage,
                cancellationToken)
            .ConfigureAwait(false);

        return ExecutionResult.Fail(
            request.ClientOrderId,
            errorCode,
            errorMessage,
            ExecutionStatus.Rejected);
    }

    private async Task<bool> IsRetryableUncertainSubmitAsync(
        string clientOrderId,
        CancellationToken cancellationToken)
    {
        var entry = await _idempotencyStore
            .GetAsync(clientOrderId, cancellationToken)
            .ConfigureAwait(false);
        if (entry?.IsUncertainSubmit != true)
        {
            return false;
        }

        var persistedOrder = await _orderRepository
            .GetByClientOrderIdAsync(clientOrderId, cancellationToken)
            .ConfigureAwait(false);

        return persistedOrder is
        {
            Status: OrderStatus.Pending,
            ExchangeOrderId: null
        };
    }

    private async Task<OrderSigningPayload?> GetRequiredSigningPayloadForRetryAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var signingPayload = await _idempotencyStore
            .GetOrderSigningPayloadAsync(request.ClientOrderId, cancellationToken)
            .ConfigureAwait(false);
        if (signingPayload is not null)
        {
            return signingPayload;
        }

        _logger.LogWarning(
            "Skipping uncertain submit retry because persisted signing payload is unavailable: ClientOrderId={ClientOrderId}",
            request.ClientOrderId);

        return null;
    }

    private async Task<ExecutionResult?> TryBlockForComplianceAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var check = _complianceGuard.CheckOrderPlacement(_options.Mode);
        if (check.Issues.Count == 0)
        {
            return null;
        }

        foreach (var issue in check.Issues)
        {
            if (issue.Severity == ComplianceSeverity.Warning)
            {
                _logger.LogWarning("Compliance warning: {Code} {Message}", issue.Code, issue.Message);
            }
            else
            {
                _logger.LogError("Compliance issue: {Code} {Message}", issue.Code, issue.Message);
            }
        }

        if (!check.BlocksOrders)
        {
            await RecordLiveComplianceWarningsAsync(request, check.Issues, cancellationToken)
                .ConfigureAwait(false);
            return null;
        }

        var message = string.Join("; ", check.Issues
            .Where(issue => issue.BlocksLiveOrders)
            .Select(issue => $"{issue.Code}: {issue.Message}"));
        if (string.IsNullOrWhiteSpace(message))
        {
            message = "Compliance guard blocked Live order placement.";
        }

        TradingMetrics.RecordOrderRejected(request.StrategyId, "live", "compliance_blocked");

        var rejectedOrder = CreateOrderDto(
            request,
            exchangeOrderId: null,
            filledQuantity: 0m,
            ExecutionStatus.Rejected,
            message);
        await UpsertOrderAsync(rejectedOrder, cancellationToken).ConfigureAwait(false);

        await _auditLogger.LogOrderRejectedAsync(
                rejectedOrder.Id,
                request.ClientOrderId,
                request.StrategyId ?? string.Empty,
                request.MarketId,
                "COMPLIANCE_BLOCKED",
                message,
                request.CorrelationId,
                cancellationToken)
            .ConfigureAwait(false);

        await _riskManager.RecordOrderErrorAsync(
                request.StrategyId ?? "unknown",
                request.ClientOrderId,
                "COMPLIANCE_BLOCKED",
                message,
                cancellationToken)
            .ConfigureAwait(false);

        return ExecutionResult.Fail(
            request.ClientOrderId,
            "COMPLIANCE_BLOCKED",
            message,
            ExecutionStatus.Rejected);
    }

    private async Task<ExecutionResult?> TryBlockForLiveArmingAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var status = await _liveArmingService.RequireArmedAsync(cancellationToken).ConfigureAwait(false);
        if (status.IsArmed)
        {
            return null;
        }

        var message = status.BlockingReasons.Count > 0
            ? string.Join("; ", status.BlockingReasons)
            : status.Reason;
        if (string.IsNullOrWhiteSpace(message))
        {
            message = $"Live trading is not armed. State: {status.State}.";
        }

        _logger.LogError(
            "Live order blocked because Live trading is not armed: State={State}, ClientOrderId={ClientOrderId}",
            status.State,
            request.ClientOrderId);

        TradingMetrics.RecordOrderRejected(request.StrategyId, "live", "live_not_armed");

        var rejectedOrder = CreateOrderDto(
            request,
            exchangeOrderId: null,
            filledQuantity: 0m,
            ExecutionStatus.Rejected,
            message);
        await UpsertOrderAsync(rejectedOrder, cancellationToken).ConfigureAwait(false);

        await _auditLogger.LogOrderRejectedAsync(
                rejectedOrder.Id,
                request.ClientOrderId,
                request.StrategyId ?? string.Empty,
                request.MarketId,
                "LIVE_NOT_ARMED",
                message,
                request.CorrelationId,
                cancellationToken)
            .ConfigureAwait(false);

        await _riskManager.RecordOrderErrorAsync(
                request.StrategyId ?? "unknown",
                request.ClientOrderId,
                "LIVE_NOT_ARMED",
                message,
                cancellationToken)
            .ConfigureAwait(false);

        return ExecutionResult.Fail(
            request.ClientOrderId,
            "LIVE_NOT_ARMED",
            message,
            ExecutionStatus.Rejected);
    }

    private async Task RecordLiveComplianceWarningsAsync(
        ExecutionRequest request,
        IEnumerable<ComplianceIssue> issues,
        CancellationToken cancellationToken)
    {
        foreach (var issue in issues.Where(issue => issue.Severity == ComplianceSeverity.Warning))
        {
            var contextJson = JsonSerializer.Serialize(new
            {
                request.ClientOrderId,
                request.MarketId,
                request.TokenId,
                request.CorrelationId,
                ExecutionMode = _options.Mode.ToString(),
                issue.BlocksLiveOrders
            });

            await _riskEventRepository.AddAsync(
                    issue.Code,
                    RiskSeverity.Warning,
                    issue.Message,
                    request.StrategyId,
                    contextJson,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<OrderTrackingEntry?> ResolveActionableTrackingEntryAsync(
        string clientOrderId,
        CancellationToken cancellationToken)
    {
        var entry = await _idempotencyStore
            .GetAsync(clientOrderId, cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(entry?.ExchangeOrderId))
        {
            return entry;
        }

        var persisted = await _orderRepository
            .GetByClientOrderIdAsync(clientOrderId, cancellationToken)
            .ConfigureAwait(false);
        if (persisted is null ||
            !IsActionableOrderStatus(persisted.Status) ||
            string.IsNullOrWhiteSpace(persisted.ClientOrderId) ||
            string.IsNullOrWhiteSpace(persisted.ExchangeOrderId))
        {
            return entry;
        }

        var restored = CreateTrackingEntryFromPersistedOrder(persisted);
        await _idempotencyStore.SeedAsync(restored, cancellationToken).ConfigureAwait(false);
        return restored;
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

    private static bool IsActionableOrderStatus(OrderStatus status) =>
        status is OrderStatus.Pending or OrderStatus.Open or OrderStatus.PartiallyFilled;

    private async Task<ExecutionResult?> TryReturnPersistedOrderResultAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var persisted = await _orderRepository
            .GetByClientOrderIdAsync(request.ClientOrderId, cancellationToken)
            .ConfigureAwait(false);
        if (persisted is null)
        {
            return null;
        }

        var requestHash = ExecutionRequestHasher.Compute(request);
        var persistedHash = ExecutionRequestHasher.Compute(persisted);
        if (!string.Equals(requestHash, persistedHash, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Durable idempotency conflict: ClientOrderId={ClientOrderId}",
                request.ClientOrderId);
            TradingMetrics.RecordOrderRejected(request.StrategyId, "live", "idempotency_conflict");

            return ExecutionResult.Fail(
                request.ClientOrderId,
                "IDEMPOTENCY_CONFLICT",
                $"ClientOrderId '{request.ClientOrderId}' already exists with different request parameters.");
        }

        var status = ToExecutionStatus(persisted.Status);
        if (persisted.Status == OrderStatus.Pending && string.IsNullOrWhiteSpace(persisted.ExchangeOrderId))
        {
            return null;
        }

        _logger.LogInformation(
            "Returning persisted order result for durable idempotency key: ClientOrderId={ClientOrderId}, Status={Status}, ExchangeOrderId={ExchangeOrderId}",
            request.ClientOrderId,
            persisted.Status,
            persisted.ExchangeOrderId);

        if (persisted.Status == OrderStatus.Rejected)
        {
            return ExecutionResult.Fail(
                request.ClientOrderId,
                "ORDER_REJECTED",
                persisted.RejectionReason ?? "Order was previously rejected.",
                ExecutionStatus.Rejected);
        }

        return ExecutionResult.Succeed(
            request.ClientOrderId,
            persisted.ExchangeOrderId ?? string.Empty,
            status,
            persisted.FilledQuantity);
    }

    private async Task<BatchOrderPreparation> TryPrepareBatchOrderAsync(
        ExecutionRequest request,
        int index,
        IDictionary<string, int> reservationsByMarket,
        CancellationToken cancellationToken)
    {
        var validationError = request.Validate();
        if (validationError is not null)
        {
            return BatchOrderPreparation.FromResult(ExecutionResult.Fail(
                request.ClientOrderId,
                "VALIDATION_ERROR",
                validationError));
        }

        var requestHash = ExecutionRequestHasher.Compute(request);
        var ttl = TimeSpan.FromSeconds(_options.IdempotencyTtlSeconds);
        var isRetryingUncertainSubmit = false;
        var isNew = false;

        var persistedResult = await TryReturnPersistedOrderResultAsync(request, cancellationToken).ConfigureAwait(false);
        if (persistedResult is not null)
        {
            return BatchOrderPreparation.FromResult(persistedResult);
        }

        var liveArmingBlock = await TryBlockForLiveArmingAsync(request, cancellationToken).ConfigureAwait(false);
        if (liveArmingBlock is not null)
        {
            return BatchOrderPreparation.FromResult(liveArmingBlock);
        }

        try
        {
            string? existingExchangeOrderId;
            (isNew, existingExchangeOrderId) = await _idempotencyStore
                .TryAddAsync(request.ClientOrderId, requestHash, ttl, cancellationToken)
                .ConfigureAwait(false);

            if (!isNew)
            {
                if (existingExchangeOrderId is not null)
                {
                    return BatchOrderPreparation.FromResult(ExecutionResult.Succeed(
                        request.ClientOrderId,
                        existingExchangeOrderId,
                        ExecutionStatus.Accepted));
                }

                if (await IsRetryableUncertainSubmitAsync(request.ClientOrderId, cancellationToken).ConfigureAwait(false))
                {
                    isRetryingUncertainSubmit = true;
                }
                else
                {
                    return BatchOrderPreparation.FromResult(ExecutionResult.Succeed(
                        request.ClientOrderId,
                        string.Empty,
                        ExecutionStatus.Pending));
                }
            }

            if (isNew)
            {
                await _idempotencyStore
                    .SetAuditInfoAsync(request.ClientOrderId, request.StrategyId, request.CorrelationId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (IdempotencyConflictException ex)
        {
            _logger.LogWarning(ex, "幂等性冲突: ClientOrderId={ClientOrderId}", request.ClientOrderId);
            TradingMetrics.RecordOrderRejected(request.StrategyId, "live", "idempotency_conflict");
            return BatchOrderPreparation.FromResult(ExecutionResult.Fail(
                request.ClientOrderId,
                "IDEMPOTENCY_CONFLICT",
                ex.Message));
        }

        if (isNew)
        {
            var complianceBlock = await TryBlockForComplianceAsync(request, cancellationToken).ConfigureAwait(false);
            if (complianceBlock is not null)
            {
                return BatchOrderPreparation.FromResult(complianceBlock);
            }
        }

        if (!isRetryingUncertainSubmit)
        {
            var limitError = ValidateBatchOrderLimit(request, reservationsByMarket);
            if (limitError is not null)
            {
                TradingMetrics.RecordOrderRejected(request.StrategyId, "live", "order_limit_exceeded");

                await _idempotencyStore
                    .RemoveAsync(request.ClientOrderId, cancellationToken)
                    .ConfigureAwait(false);

                return BatchOrderPreparation.FromResult(ExecutionResult.Fail(
                    request.ClientOrderId,
                    "ORDER_LIMIT_EXCEEDED",
                    limitError));
            }

            reservationsByMarket.TryGetValue(request.MarketId, out var reservedForMarket);
            reservationsByMarket[request.MarketId] = reservedForMarket + 1;
        }

        await _idempotencyStore
            .SetMarketInfoAsync(request.ClientOrderId, request.MarketId, request.TokenId, cancellationToken)
            .ConfigureAwait(false);

        var signingPayload = isNew
            ? await _idempotencyStore
                .GetOrCreateOrderSigningPayloadAsync(request.ClientOrderId, cancellationToken)
                .ConfigureAwait(false)
            : await GetRequiredSigningPayloadForRetryAsync(request, cancellationToken).ConfigureAwait(false);
        if (signingPayload is null)
        {
            return BatchOrderPreparation.FromResult(ExecutionResult.Succeed(
                request.ClientOrderId,
                string.Empty,
                ExecutionStatus.Pending));
        }

        TradingMetrics.RecordOrderSubmitted(request.StrategyId, "live");

        return BatchOrderPreparation.FromPrepared(new PreparedBatchOrder(
            index,
            request,
            BuildOrderRequest(request, signingPayload),
            signingPayload));
    }

    private string? ValidateBatchOrderLimit(
        ExecutionRequest request,
        IDictionary<string, int> reservationsByMarket)
    {
        var limitError = _limitValidator.ValidateCanPlaceOrder(request.MarketId);
        if (limitError is not null)
        {
            return limitError;
        }

        var maxOpenOrders = _options.MaxOpenOrdersPerMarket;
        if (maxOpenOrders <= 0)
        {
            return null;
        }

        reservationsByMarket.TryGetValue(request.MarketId, out var reserved);
        var openCount = _stateTracker.GetOpenOrderCount(request.MarketId);
        return openCount + reserved >= maxOpenOrders
            ? $"Open order limit exceeded for market {request.MarketId}: {openCount + reserved}/{maxOpenOrders}"
            : null;
    }

    private async Task<ExecutionResult> CompleteBatchOrderAsync(
        PreparedBatchOrder prepared,
        OrderResponse response,
        CancellationToken cancellationToken)
    {
        var request = prepared.Request;
        if (!response.Success)
        {
            _logger.LogWarning(
                "批量下单单笔被拒绝: ClientOrderId={ClientOrderId}, ErrorMsg={ErrorMsg}",
                request.ClientOrderId,
                response.ErrorMsg);

            TradingMetrics.RecordOrderRejected(request.StrategyId, "live", "order_rejected");

            return await RejectPreparedOrderAsync(
                    request,
                    prepared.SigningPayload,
                    "ORDER_REJECTED",
                    response.ErrorMsg ?? "订单被拒绝",
                    metricsReason: null,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var exchangeOrderId = response.OrderId;
        if (string.IsNullOrWhiteSpace(exchangeOrderId))
        {
            const string error = "Exchange order id is missing from successful order response.";
            return await MarkPreparedOrderUncertainAsync(
                    request,
                    prepared.SigningPayload,
                    "MISSING_EXCHANGE_ORDER_ID",
                    error,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await _idempotencyStore
            .SetExchangeOrderIdAsync(request.ClientOrderId, exchangeOrderId, cancellationToken)
            .ConfigureAwait(false);

        var status = MapPlacementStatus(response.Status);
        await UpsertOrderAsync(
            CreateOrderDto(request, exchangeOrderId, 0m, status, signingPayload: prepared.SigningPayload),
            cancellationToken).ConfigureAwait(false);

        await _stateTracker.OnOrderStateChangedAsync(new OrderStateUpdate
        {
            ClientOrderId = request.ClientOrderId,
            ExchangeOrderId = exchangeOrderId,
            MarketId = request.MarketId,
            TokenId = request.TokenId,
            Status = status,
            OriginalQuantity = request.Quantity,
            FilledQuantity = 0m
        }, cancellationToken).ConfigureAwait(false);

        await _auditLogger.LogOrderAcceptedAsync(
            OrderAuditIds.ForClientOrderId(request.ClientOrderId),
            request.ClientOrderId,
            request.StrategyId ?? string.Empty,
            request.MarketId,
            exchangeOrderId,
            request.CorrelationId,
            cancellationToken).ConfigureAwait(false);

        TradingMetrics.RecordOrderAccepted(request.StrategyId, "live");

        return ExecutionResult.Succeed(
            request.ClientOrderId,
            exchangeOrderId,
            status);
    }

    private async Task RemovePreparedBatchEntriesAsync(
        IReadOnlyList<PreparedBatchOrder> preparedOrders,
        CancellationToken cancellationToken)
    {
        foreach (var prepared in preparedOrders)
        {
            await _idempotencyStore
                .RemoveAsync(prepared.Request.ClientOrderId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task ReleasePreparedBatchOrderStatesAsync(
        IReadOnlyList<PreparedBatchOrder> preparedOrders,
        CancellationToken cancellationToken)
    {
        foreach (var prepared in preparedOrders)
        {
            await _stateTracker.OnOrderStateChangedAsync(new OrderStateUpdate
            {
                ClientOrderId = prepared.Request.ClientOrderId,
                ExchangeOrderId = string.Empty,
                MarketId = prepared.Request.MarketId,
                TokenId = prepared.Request.TokenId,
                Status = ExecutionStatus.Rejected,
                OriginalQuantity = prepared.Request.Quantity,
                FilledQuantity = 0m
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task FillSequentialFallbackResultsAsync(
        IReadOnlyList<PreparedBatchOrder> preparedOrders,
        ExecutionResult?[] results,
        CancellationToken cancellationToken)
    {
        foreach (var prepared in preparedOrders)
        {
            results[prepared.Index] = await PlaceOrderAsync(prepared.Request, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task MarkBatchOrdersUncertainAsync(
        IReadOnlyList<PreparedBatchOrder> preparedOrders,
        ExecutionResult?[] results,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        foreach (var prepared in preparedOrders)
        {
            results[prepared.Index] = await MarkPreparedOrderUncertainAsync(
                    prepared.Request,
                    prepared.SigningPayload,
                    errorCode,
                    errorMessage,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task MarkBatchOrdersRejectedAsync(
        IReadOnlyList<PreparedBatchOrder> preparedOrders,
        ExecutionResult?[] results,
        string errorCode,
        string errorMessage,
        string metricsReason,
        CancellationToken cancellationToken)
    {
        foreach (var prepared in preparedOrders)
        {
            results[prepared.Index] = await RejectPreparedOrderAsync(
                    prepared.Request,
                    prepared.SigningPayload,
                    errorCode,
                    errorMessage,
                    metricsReason,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static string BuildBatchIdempotencyKey(IReadOnlyList<PreparedBatchOrder> preparedOrders)
    {
        var joined = string.Join("|", preparedOrders.Select(order => order.Request.ClientOrderId));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return $"batch:{Convert.ToHexString(bytes)}";
    }

    private static bool IsBatchEndpointUnavailableStatus(int statusCode) =>
        statusCode is 404 or 405 or 410 or 501;

    private static bool IsDefinitiveOrderCreationRejectedStatus(int statusCode) =>
        statusCode is 400 or 401 or 403 or 404 or 405 or 410 or 422;

    private static string BuildOrderSigningKey(ExecutionRequest request) =>
        $"{request.ClientOrderId}:{ExecutionRequestHasher.Compute(request)}";

    private async Task<IReadOnlyList<ExecutionResult>> PlaceOrdersSequentiallyAsync(
        IReadOnlyList<ExecutionRequest> requests,
        CancellationToken cancellationToken)
    {
        var results = new List<ExecutionResult>(requests.Count);
        foreach (var request in requests)
        {
            results.Add(await PlaceOrderAsync(request, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private OrderDto CreateOrderDto(
        ExecutionRequest request,
        string? exchangeOrderId,
        decimal filledQuantity,
        ExecutionStatus status,
        string? rejectionReason = null,
        OrderSigningPayload? signingPayload = null)
    {
        return new OrderDto(
            Id: OrderAuditIds.ForClientOrderId(request.ClientOrderId),
            TradingAccountId: _accountContext.TradingAccountId,
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
            FilledQuantity: filledQuantity,
            Status: ToOrderStatus(status),
            RejectionReason: rejectionReason,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            OrderSalt: signingPayload?.Salt,
            OrderTimestamp: signingPayload?.Timestamp);
    }

    private async Task MarkPersistedOrderStatusAsync(
        string clientOrderId,
        ExecutionStatus status,
        CancellationToken cancellationToken)
    {
        var existing = await _orderRepository.GetByClientOrderIdAsync(clientOrderId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return;
        }

        await _orderRepository.UpdateAsync(existing with
        {
            Status = ToOrderStatus(status),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task UpsertOrderAsync(OrderDto order, CancellationToken cancellationToken)
    {
        var existing = await _orderRepository.GetByIdAsync(order.Id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            await _orderRepository.AddAsync(order, cancellationToken).ConfigureAwait(false);
            return;
        }

        await _orderRepository.UpdateAsync(order with
        {
            CreatedAtUtc = existing.CreatedAtUtc,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        }, cancellationToken).ConfigureAwait(false);
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

    private static string MapTimeInForce(TimeInForce tif) => tif switch
    {
        TimeInForce.Gtc => "GTC",
        TimeInForce.Gtd => "GTD",
        // Polymarket 原生支持 FAK 和 FOK
        TimeInForce.Fak => "FAK",
        TimeInForce.Fok => "FOK",
        _ => "GTC"
    };

    private static ExecutionStatus MapPlacementStatus(string? status) => status?.ToUpperInvariant() switch
    {
        "LIVE" or "OPEN" => ExecutionStatus.Accepted,
        "MATCHED" or "FILLED" => ExecutionStatus.Accepted,
        "CANCELLED" or "CANCELED" => ExecutionStatus.Cancelled,
        _ => ExecutionStatus.Pending
    };

    private static ExecutionStatus MapOrderInfoStatus(string? status) => status?.ToUpperInvariant() switch
    {
        "LIVE" or "OPEN" => ExecutionStatus.Accepted,
        "MATCHED" or "FILLED" => ExecutionStatus.Filled,
        "CANCELLED" or "CANCELED" => ExecutionStatus.Cancelled,
        _ => ExecutionStatus.Pending
    };

    private sealed record PreparedBatchOrder(
        int Index,
        ExecutionRequest Request,
        OrderRequest OrderRequest,
        OrderSigningPayload SigningPayload);

    private sealed record BatchOrderPreparation(
        PreparedBatchOrder? PreparedOrder,
        ExecutionResult? Result)
    {
        public static BatchOrderPreparation FromPrepared(PreparedBatchOrder preparedOrder) =>
            new(preparedOrder, null);

        public static BatchOrderPreparation FromResult(ExecutionResult result) =>
            new(null, result);
    }
}
