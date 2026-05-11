using System.Globalization;
using Autotrade.Trading.Application.Audit;
using Autotrade.Trading.Application.Contract.Audit;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Application.Contract.UserEvents;
using Autotrade.Trading.Application.Execution;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Trading.Infra.BackgroundJobs.Workers;

public sealed class UserOrderEventWorker : BackgroundService
{
    private readonly IUserOrderEventSource _eventSource;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOrderStateTracker _stateTracker;
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly TradingAccountContext _accountContext;
    private readonly ExecutionOptions _options;
    private readonly ILogger<UserOrderEventWorker> _logger;
    private IDisposable? _orderSubscription;
    private IDisposable? _tradeSubscription;

    public UserOrderEventWorker(
        IUserOrderEventSource eventSource,
        IServiceScopeFactory scopeFactory,
        IOrderStateTracker stateTracker,
        IIdempotencyStore idempotencyStore,
        TradingAccountContext accountContext,
        IOptions<ExecutionOptions> options,
        ILogger<UserOrderEventWorker> logger)
    {
        _eventSource = eventSource ?? throw new ArgumentNullException(nameof(eventSource));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _stateTracker = stateTracker ?? throw new ArgumentNullException(nameof(stateTracker));
        _idempotencyStore = idempotencyStore ?? throw new ArgumentNullException(nameof(idempotencyStore));
        _accountContext = accountContext ?? throw new ArgumentNullException(nameof(accountContext));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.Mode != ExecutionMode.Live || !_options.EnableUserOrderEvents)
        {
            _logger.LogInformation("CLOB user order event worker disabled for mode {Mode}", _options.Mode);
            return;
        }

        _orderSubscription = _eventSource.OnOrder(HandleOrderEventAsync);
        _tradeSubscription = _eventSource.OnTrade(HandleTradeEventAsync);

        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.UserOrderEventSubscriptionRefreshSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var subscribedCount = await RefreshSubscriptionsAsync(stoppingToken).ConfigureAwait(false);
                if (subscribedCount > 0 && !_eventSource.IsConnected)
                {
                    await _eventSource.ConnectAsync(stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CLOB user order event worker cycle failed");
            }

            await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _orderSubscription?.Dispose();
        _tradeSubscription?.Dispose();
        await _eventSource.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> RefreshSubscriptionsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var orderRepository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var openOrders = await orderRepository.GetOpenOrdersAsync(cancellationToken).ConfigureAwait(false);
        var markets = openOrders
            .Select(order => order.MarketId)
            .Where(marketId => !string.IsNullOrWhiteSpace(marketId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (markets.Length > 0)
        {
            await _eventSource.SubscribeMarketsAsync(markets, cancellationToken).ConfigureAwait(false);
        }

        return markets.Length;
    }

    private async Task HandleOrderEventAsync(UserOrderEvent userEvent, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var orderRepository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var auditLogger = scope.ServiceProvider.GetRequiredService<IOrderAuditLogger>();
        var riskManager = scope.ServiceProvider.GetRequiredService<IRiskManager>();

        var order = await ResolveOrderAsync(orderRepository, userEvent.ExchangeOrderId, cancellationToken)
            .ConfigureAwait(false);
        if (order is null)
        {
            _logger.LogDebug("Ignoring user order event for unknown exchange order id: {ExchangeOrderId}",
                userEvent.ExchangeOrderId);
            return;
        }

        var previousStatus = order.Status;
        var previousFilled = order.FilledQuantity;
        var eventFilled = TryParseDecimalInvariant(userEvent.SizeMatched, out var parsedFilled)
            ? parsedFilled
            : order.FilledQuantity;
        var filled = Math.Max(order.FilledQuantity, eventFilled);
        var price = TryParseDecimalInvariant(userEvent.Price, out var parsedPrice)
            ? parsedPrice
            : order.Price;
        var quantity = TryParseDecimalInvariant(userEvent.OriginalSize, out var parsedQuantity)
            ? parsedQuantity
            : order.Quantity;
        var mappedStatus = MapOrderStatus(userEvent.Status, userEvent.Type, filled, quantity, previousStatus);
        var nextStatus = MergeOrderStatus(previousStatus, mappedStatus);

        var updated = order with
        {
            ExchangeOrderId = userEvent.ExchangeOrderId,
            MarketId = string.IsNullOrWhiteSpace(userEvent.MarketId) ? order.MarketId : userEvent.MarketId!,
            TokenId = string.IsNullOrWhiteSpace(userEvent.TokenId) ? order.TokenId : userEvent.TokenId,
            FilledQuantity = filled,
            Status = nextStatus,
            UpdatedAtUtc = MaxTimestamp(order.UpdatedAtUtc, userEvent.TimestampUtc)
        };

        await orderRepository.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);
        await PublishStateAndRiskAsync(updated, price, riskManager, cancellationToken).ConfigureAwait(false);
        await LogOrderAuditIfChangedAsync(
                auditLogger,
                updated,
                previousStatus,
                previousFilled,
                price,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task HandleTradeEventAsync(UserTradeEvent userEvent, CancellationToken cancellationToken)
    {
        if (IsFailedTradeStatus(userEvent.Status))
        {
            _logger.LogWarning(
                "Ignoring failed CLOB user trade event: TradeId={TradeId}, Status={Status}",
                userEvent.ExchangeTradeId,
                userEvent.Status);
            return;
        }

        if (!IsConfirmedTradeStatus(userEvent.Status))
        {
            _logger.LogDebug(
                "Ignoring non-final CLOB user trade event until confirmation: TradeId={TradeId}, Status={Status}",
                userEvent.ExchangeTradeId,
                userEvent.Status);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var orderRepository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var tradeRepository = scope.ServiceProvider.GetRequiredService<ITradeRepository>();
        var auditLogger = scope.ServiceProvider.GetRequiredService<IOrderAuditLogger>();
        var riskManager = scope.ServiceProvider.GetRequiredService<IRiskManager>();

        var existingTrade = await tradeRepository.GetByExchangeTradeIdAsync(
                userEvent.ExchangeTradeId,
                cancellationToken)
            .ConfigureAwait(false);
        if (existingTrade is not null)
        {
            return;
        }

        var order = await ResolveOrderFromTradeAsync(orderRepository, userEvent, cancellationToken)
            .ConfigureAwait(false);
        if (order is null)
        {
            _logger.LogDebug("Ignoring user trade event for unknown order: TradeId={TradeId}",
                userEvent.ExchangeTradeId);
            return;
        }

        var makerOrder = userEvent.MakerOrders.FirstOrDefault(maker =>
            !string.IsNullOrWhiteSpace(maker.ExchangeOrderId) &&
            string.Equals(maker.ExchangeOrderId, order.ExchangeOrderId, StringComparison.Ordinal));

        var sideText = makerOrder is null ? userEvent.Side : makerOrder.Side;
        var outcomeText = makerOrder?.Outcome ?? userEvent.Outcome;
        var tokenId = makerOrder?.AssetId ?? userEvent.TokenId ?? order.TokenId ?? string.Empty;
        var quantityText = makerOrder?.MatchedAmount ?? userEvent.Size;

        if (!TryParseTrade(userEvent, order, sideText, outcomeText, quantityText, out var side, out var outcome, out var price, out var quantity, out var fee))
        {
            _logger.LogWarning("Unable to parse CLOB user trade event: TradeId={TradeId}", userEvent.ExchangeTradeId);
            return;
        }

        var previousStatus = order.Status;
        var previousFilled = order.FilledQuantity;
        var clientOrderId = order.ClientOrderId ?? order.Id.ToString("N");
        var persistedTrades = await tradeRepository.GetByClientOrderIdAsync(clientOrderId, cancellationToken)
            .ConfigureAwait(false);
        var ledgerFilled = persistedTrades.Sum(trade => trade.Quantity) + quantity;
        var filled = Math.Min(order.Quantity, Math.Max(order.FilledQuantity, ledgerFilled));
        var mappedStatus = filled >= order.Quantity ? OrderStatus.Filled : OrderStatus.PartiallyFilled;
        var nextStatus = MergeOrderStatus(previousStatus, mappedStatus);
        var updated = order with
        {
            FilledQuantity = filled,
            Status = nextStatus,
            UpdatedAtUtc = MaxTimestamp(order.UpdatedAtUtc, userEvent.TimestampUtc)
        };

        await orderRepository.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);
        await PublishStateAndRiskAsync(updated, price, riskManager, cancellationToken).ConfigureAwait(false);
        await LogOrderAuditIfChangedAsync(
                auditLogger,
                updated,
                previousStatus,
                previousFilled,
                price,
                cancellationToken)
            .ConfigureAwait(false);

        await auditLogger.LogTradeAsync(
                OrderAuditIds.ForClientOrderId(order.ClientOrderId ?? order.Id.ToString("N")),
                _accountContext.TradingAccountId,
                order.ClientOrderId ?? order.Id.ToString("N"),
                order.StrategyId ?? string.Empty,
                order.MarketId,
                tokenId,
                outcome,
                side,
                price,
                quantity,
                userEvent.ExchangeTradeId,
                fee,
                order.CorrelationId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<OrderDto?> ResolveOrderAsync(
        IOrderRepository orderRepository,
        string exchangeOrderId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(exchangeOrderId))
        {
            return null;
        }

        var order = await orderRepository.GetByExchangeOrderIdAsync(exchangeOrderId, cancellationToken)
            .ConfigureAwait(false);
        if (order is not null)
        {
            return order;
        }

        var clientOrderId = await _idempotencyStore.FindClientOrderIdByExchangeIdAsync(exchangeOrderId, cancellationToken)
            .ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(clientOrderId)
            ? null
            : await orderRepository.GetByClientOrderIdAsync(clientOrderId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OrderDto?> ResolveOrderFromTradeAsync(
        IOrderRepository orderRepository,
        UserTradeEvent userEvent,
        CancellationToken cancellationToken)
    {
        var candidateOrderIds = new[] { userEvent.ExchangeOrderId }
            .Concat(userEvent.MakerOrders.Select(order => order.ExchangeOrderId))
            .Where(orderId => !string.IsNullOrWhiteSpace(orderId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var exchangeOrderId in candidateOrderIds)
        {
            var order = await ResolveOrderAsync(orderRepository, exchangeOrderId!, cancellationToken)
                .ConfigureAwait(false);
            if (order is not null)
            {
                return order;
            }
        }

        return null;
    }

    private async Task PublishStateAndRiskAsync(
        OrderDto order,
        decimal? fillPrice,
        IRiskManager riskManager,
        CancellationToken cancellationToken)
    {
        var status = ToExecutionStatus(order.Status);
        await _stateTracker.OnOrderStateChangedAsync(new OrderStateUpdate
            {
                ClientOrderId = order.ClientOrderId ?? order.Id.ToString("N"),
                ExchangeOrderId = order.ExchangeOrderId ?? string.Empty,
                MarketId = order.MarketId,
                TokenId = order.TokenId,
                Status = status,
                OriginalQuantity = order.Quantity,
                FilledQuantity = order.FilledQuantity,
                AveragePrice = fillPrice
            },
            cancellationToken).ConfigureAwait(false);

        await riskManager.RecordOrderUpdateAsync(new RiskOrderUpdate
            {
                ClientOrderId = order.ClientOrderId ?? order.Id.ToString("N"),
                StrategyId = order.StrategyId,
                Status = status,
                FilledQuantity = order.FilledQuantity,
                OriginalQuantity = order.Quantity,
                FilledPrice = fillPrice,
                MarketId = order.MarketId
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task LogOrderAuditIfChangedAsync(
        IOrderAuditLogger auditLogger,
        OrderDto order,
        OrderStatus previousStatus,
        decimal previousFilled,
        decimal fillPrice,
        CancellationToken cancellationToken)
    {
        var clientOrderId = order.ClientOrderId ?? order.Id.ToString("N");
        var orderId = OrderAuditIds.ForClientOrderId(clientOrderId);
        var strategyId = order.StrategyId ?? string.Empty;

        if (order.Status is OrderStatus.Open && previousStatus is OrderStatus.Pending)
        {
            await auditLogger.LogOrderAcceptedAsync(
                    orderId,
                    clientOrderId,
                    strategyId,
                    order.MarketId,
                    order.ExchangeOrderId,
                    order.CorrelationId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (order.FilledQuantity > previousFilled && order.FilledQuantity > 0m)
        {
            var filledDelta = order.FilledQuantity - previousFilled;
            await auditLogger.LogOrderFilledAsync(
                    orderId,
                    clientOrderId,
                    strategyId,
                    order.MarketId,
                    filledDelta,
                    fillPrice,
                    order.Status != OrderStatus.Filled,
                    order.CorrelationId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (order.Status != previousStatus)
        {
            if (order.Status == OrderStatus.Cancelled)
            {
                await auditLogger.LogOrderCancelledAsync(
                        orderId,
                        clientOrderId,
                        strategyId,
                        order.MarketId,
                        order.CorrelationId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (order.Status == OrderStatus.Expired)
            {
                await auditLogger.LogOrderExpiredAsync(
                        orderId,
                        clientOrderId,
                        strategyId,
                        order.MarketId,
                        order.CorrelationId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (order.Status == OrderStatus.Rejected)
            {
                await auditLogger.LogOrderRejectedAsync(
                        orderId,
                        clientOrderId,
                        strategyId,
                        order.MarketId,
                        "USER_WS_REJECTED",
                        order.RejectionReason ?? "Rejected by CLOB user event",
                        order.CorrelationId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private static OrderStatus MapOrderStatus(
        string? status,
        string? type,
        decimal filled,
        decimal quantity,
        OrderStatus fallback)
    {
        var normalizedStatus = Normalize(status);
        var normalizedType = Normalize(type);

        if (normalizedStatus.Contains("CANCEL", StringComparison.Ordinal) ||
            normalizedType.Contains("CANCEL", StringComparison.Ordinal))
        {
            return OrderStatus.Cancelled;
        }

        if (normalizedStatus.Contains("EXPIRE", StringComparison.Ordinal))
        {
            return OrderStatus.Expired;
        }

        if (normalizedStatus.Contains("REJECT", StringComparison.Ordinal))
        {
            return OrderStatus.Rejected;
        }

        if (quantity > 0m && filled >= quantity)
        {
            return OrderStatus.Filled;
        }

        if (filled > 0m)
        {
            return OrderStatus.PartiallyFilled;
        }

        if (normalizedStatus.Contains("LIVE", StringComparison.Ordinal) ||
            normalizedStatus.Contains("OPEN", StringComparison.Ordinal) ||
            normalizedStatus.Contains("UNMATCHED", StringComparison.Ordinal) ||
            normalizedType.Contains("PLACEMENT", StringComparison.Ordinal))
        {
            return OrderStatus.Open;
        }

        return fallback;
    }

    private static OrderStatus MergeOrderStatus(OrderStatus current, OrderStatus incoming)
    {
        if (IsTerminalStatus(current))
        {
            return current;
        }

        if (incoming == OrderStatus.Pending && current != OrderStatus.Pending)
        {
            return current;
        }

        if (incoming == OrderStatus.Open && current == OrderStatus.PartiallyFilled)
        {
            return current;
        }

        return incoming;
    }

    private static bool IsTerminalStatus(OrderStatus status) =>
        status is OrderStatus.Filled or OrderStatus.Cancelled or OrderStatus.Rejected or OrderStatus.Expired;

    private static bool IsConfirmedTradeStatus(string? status) =>
        Normalize(status) is "CONFIRMED";

    private static bool IsFailedTradeStatus(string? status) =>
        Normalize(status) is "FAILED";

    private static DateTimeOffset MaxTimestamp(DateTimeOffset left, DateTimeOffset right) =>
        left >= right ? left : right;

    private static bool TryParseTrade(
        UserTradeEvent userEvent,
        OrderDto order,
        string? sideText,
        string? outcomeText,
        string? quantityText,
        out OrderSide side,
        out OutcomeSide outcome,
        out decimal price,
        out decimal quantity,
        out decimal fee)
    {
        side = order.Side;
        outcome = order.Outcome;
        price = 0m;
        quantity = 0m;
        fee = 0m;

        if (!string.IsNullOrWhiteSpace(sideText) &&
            Enum.TryParse<OrderSide>(NormalizeSide(sideText), ignoreCase: true, out var parsedSide))
        {
            side = parsedSide;
        }

        if (!string.IsNullOrWhiteSpace(outcomeText) &&
            Enum.TryParse<OutcomeSide>(outcomeText, ignoreCase: true, out var parsedOutcome))
        {
            outcome = parsedOutcome;
        }

        if (!TryParseDecimalInvariant(userEvent.Price, out price) ||
            !TryParseDecimalInvariant(quantityText, out quantity) ||
            quantity <= 0m)
        {
            return false;
        }

        if (TryParseDecimalInvariant(userEvent.FeeRateBps, out var feeRateBps) && feeRateBps > 0m)
        {
            fee = price * quantity * feeRateBps / 10000m;
        }

        return true;
    }

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

    private static string NormalizeSide(string side) => Normalize(side) switch
    {
        "BUY" => nameof(OrderSide.Buy),
        "SELL" => nameof(OrderSide.Sell),
        _ => side
    };

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static bool TryParseDecimalInvariant(string? value, out decimal result) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
}
