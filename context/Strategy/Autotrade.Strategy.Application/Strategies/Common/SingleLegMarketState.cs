using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Strategy.Application.Strategies.Common;

internal sealed class SingleLegMarketState
{
    private const decimal QuantityTolerance = 0.000001m;
    private readonly Dictionary<string, OrderTracking> _orders = new(StringComparer.OrdinalIgnoreCase);

    public SingleLegMarketState(string marketId)
    {
        MarketId = marketId;
    }

    public string MarketId { get; }

    public DateTimeOffset? LastEntryAttemptUtc { get; set; }

    public DateTimeOffset? LastExitAttemptUtc { get; set; }

    public DateTimeOffset? EntryFilledUtc { get; private set; }

    public OutcomeSide? Outcome { get; private set; }

    public string? TokenId { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal AverageEntryPrice { get; private set; }

    public decimal OpenNotional => Quantity * AverageEntryPrice;

    public bool HasPosition => Quantity > QuantityTolerance;

    public bool IsFlat => !HasPosition;

    public bool HasOpenEntryOrder => HasOpenOrder(StrategySignalType.Entry);

    public bool HasOpenExitOrder => HasOpenOrder(StrategySignalType.Exit);

    public void ApplyOrderUpdate(StrategyOrderUpdate update)
    {
        if (!_orders.TryGetValue(update.ClientOrderId, out var order))
        {
            order = new OrderTracking(update.SignalType, update.Side, update.Outcome, update.Leg);
            _orders[update.ClientOrderId] = order;
        }

        var filled = Math.Max(order.FilledQuantity, update.FilledQuantity);
        var fillDelta = filled - order.FilledQuantity;

        order.SignalType = update.SignalType;
        order.Side = update.Side;
        order.Outcome = update.Outcome;
        order.Leg = update.Leg;
        order.Status = update.Status;
        order.FilledQuantity = filled;

        if (fillDelta <= QuantityTolerance)
        {
            return;
        }

        if (update.Side == OrderSide.Buy)
        {
            ApplyBuyFill(update, fillDelta);
        }
        else
        {
            ApplySellFill(fillDelta);
        }
    }

    public IReadOnlyList<string> GetOpenOrderIds()
        => _orders
            .Where(item => IsOpenStatus(item.Value.Status))
            .Select(item => item.Key)
            .ToArray();

    public IReadOnlyList<string> GetOpenOrderIds(StrategySignalType signalType)
        => _orders
            .Where(item => item.Value.SignalType == signalType && IsOpenStatus(item.Value.Status))
            .Select(item => item.Key)
            .ToArray();

    public void MarkOrderCancelled(string clientOrderId)
    {
        if (_orders.TryGetValue(clientOrderId, out var order))
        {
            order.Status = ExecutionStatus.Cancelled;
        }
    }

    private bool HasOpenOrder(StrategySignalType signalType)
        => _orders.Values.Any(item => item.SignalType == signalType && IsOpenStatus(item.Status));

    private void ApplyBuyFill(StrategyOrderUpdate update, decimal fillDelta)
    {
        var previousQuantity = Quantity;
        var previousCost = AverageEntryPrice * previousQuantity;
        var fillCost = update.Price * fillDelta;
        var newQuantity = previousQuantity + fillDelta;

        Quantity = newQuantity;
        AverageEntryPrice = newQuantity <= QuantityTolerance
            ? 0m
            : (previousCost + fillCost) / newQuantity;
        Outcome = update.Outcome;
        TokenId = update.TokenId;
        EntryFilledUtc ??= update.TimestampUtc;
    }

    private void ApplySellFill(decimal fillDelta)
    {
        Quantity = Math.Max(0m, Quantity - fillDelta);

        if (Quantity <= QuantityTolerance)
        {
            Quantity = 0m;
            AverageEntryPrice = 0m;
            Outcome = null;
            TokenId = null;
            EntryFilledUtc = null;
        }
    }

    private static bool IsOpenStatus(ExecutionStatus status)
        => status is ExecutionStatus.Pending or ExecutionStatus.Accepted or ExecutionStatus.PartiallyFilled;

    private sealed class OrderTracking
    {
        public OrderTracking(
            StrategySignalType signalType,
            OrderSide side,
            OutcomeSide outcome,
            Autotrade.Trading.Application.Contract.Risk.OrderLeg leg)
        {
            SignalType = signalType;
            Side = side;
            Outcome = outcome;
            Leg = leg;
        }

        public StrategySignalType SignalType { get; set; }

        public OrderSide Side { get; set; }

        public OutcomeSide Outcome { get; set; }

        public Autotrade.Trading.Application.Contract.Risk.OrderLeg Leg { get; set; }

        public ExecutionStatus Status { get; set; }

        public decimal FilledQuantity { get; set; }
    }
}
