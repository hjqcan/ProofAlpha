using System.Text.Json;
using Autotrade.SelfImprove.Application.Contract.Episodes;
using Autotrade.SelfImprove.Domain.Entities;
using Autotrade.Strategy.Application.Decisions;
using Autotrade.Strategy.Application.Observations;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.SelfImprove.Application.Episodes;

public interface IStrategyEpisodeBuilder
{
    Task<StrategyEpisode> BuildAsync(BuildStrategyEpisodeRequest request, CancellationToken cancellationToken = default);
}

public sealed class StrategyEpisodeBuilder : IStrategyEpisodeBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IStrategyDecisionRepository _decisionRepository;
    private readonly IStrategyObservationRepository _observationRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly ITradeRepository _tradeRepository;
    private readonly IOrderEventRepository _orderEventRepository;
    private readonly IRiskEventRepository _riskEventRepository;

    public StrategyEpisodeBuilder(
        IStrategyDecisionRepository decisionRepository,
        IStrategyObservationRepository observationRepository,
        IOrderRepository orderRepository,
        ITradeRepository tradeRepository,
        IOrderEventRepository orderEventRepository,
        IRiskEventRepository riskEventRepository)
    {
        _decisionRepository = decisionRepository ?? throw new ArgumentNullException(nameof(decisionRepository));
        _observationRepository = observationRepository ?? throw new ArgumentNullException(nameof(observationRepository));
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _tradeRepository = tradeRepository ?? throw new ArgumentNullException(nameof(tradeRepository));
        _orderEventRepository = orderEventRepository ?? throw new ArgumentNullException(nameof(orderEventRepository));
        _riskEventRepository = riskEventRepository ?? throw new ArgumentNullException(nameof(riskEventRepository));
    }

    public async Task<StrategyEpisode> BuildAsync(
        BuildStrategyEpisodeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.StrategyId))
        {
            throw new ArgumentException("StrategyId cannot be empty.", nameof(request));
        }

        if (request.WindowEndUtc <= request.WindowStartUtc)
        {
            throw new ArgumentException("WindowEndUtc must be after WindowStartUtc.", nameof(request));
        }

        var limit = Math.Clamp(request.Limit, 1, 50000);

        var decisions = await _decisionRepository.QueryAsync(
            new StrategyDecisionQuery(
                StrategyId: request.StrategyId,
                MarketId: request.MarketId,
                FromUtc: request.WindowStartUtc,
                ToUtc: request.WindowEndUtc,
                Limit: limit),
            cancellationToken).ConfigureAwait(false);

        var observations = await _observationRepository.QueryAsync(
            new StrategyObservationQuery(
                StrategyId: request.StrategyId,
                MarketId: request.MarketId,
                FromUtc: request.WindowStartUtc,
                ToUtc: request.WindowEndUtc,
                Limit: limit),
            cancellationToken).ConfigureAwait(false);

        var orders = await _orderRepository.GetByStrategyIdAsync(
            request.StrategyId,
            request.WindowStartUtc,
            request.WindowEndUtc,
            limit,
            cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(request.MarketId))
        {
            orders = orders
                .Where(order => string.Equals(order.MarketId, request.MarketId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var trades = await _tradeRepository.GetByStrategyIdAsync(
            request.StrategyId,
            request.WindowStartUtc,
            request.WindowEndUtc,
            limit,
            cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(request.MarketId))
        {
            trades = trades
                .Where(trade => string.Equals(trade.MarketId, request.MarketId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var orderEvents = await _orderEventRepository.GetByStrategyIdAsync(
            request.StrategyId,
            request.WindowStartUtc,
            request.WindowEndUtc,
            limit,
            cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(request.MarketId))
        {
            orderEvents = orderEvents
                .Where(orderEvent => string.Equals(orderEvent.MarketId, request.MarketId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var riskEvents = await _riskEventRepository.QueryAsync(
            request.StrategyId,
            request.WindowStartUtc,
            request.WindowEndUtc,
            limit,
            cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(request.MarketId))
        {
            riskEvents = riskEvents
                .Where(riskEvent => string.Equals(riskEvent.MarketId, request.MarketId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var pnl = await _tradeRepository.GetPnLSummaryAsync(
            request.StrategyId,
            request.WindowStartUtc,
            request.WindowEndUtc,
            cancellationToken).ConfigureAwait(false);

        var filledOrders = orders.Count(order => order.Status == OrderStatus.Filled);
        var rejectedOrders = orders.Count(order => order.Status == OrderStatus.Rejected);
        var timeoutObservations = observations.Count(obs =>
            string.Equals(obs.Outcome, "Timeout", StringComparison.OrdinalIgnoreCase));
        var orderCount = Math.Max(orders.Count, 0);
        var fillRate = orderCount == 0 ? 0 : (decimal)filledOrders / orderCount;
        var rejectRate = orderCount == 0
            ? riskEvents.Count > 0 ? 1 : 0
            : (decimal)(rejectedOrders + riskEvents.Count) / Math.Max(1, orderCount + riskEvents.Count);
        var timeoutRate = observations.Count == 0 ? 0 : (decimal)timeoutObservations / observations.Count;
        var maxOpenExposure = orders
            .Where(order => order.Status is OrderStatus.Open or OrderStatus.PartiallyFilled)
            .Select(order => order.Price * Math.Max(0, order.Quantity - order.FilledQuantity))
            .DefaultIfEmpty(0m)
            .Max();

        var cumulative = 0m;
        var peak = 0m;
        var drawdown = 0m;
        foreach (var trade in trades.OrderBy(trade => trade.CreatedAtUtc))
        {
            cumulative += trade.Side == OrderSide.Sell ? trade.NetNotional : -trade.NetNotional;
            peak = Math.Max(peak, cumulative);
            drawdown = Math.Max(drawdown, peak - cumulative);
        }

        var configVersion = observations.FirstOrDefault()?.ConfigVersion
            ?? decisions.FirstOrDefault()?.ConfigVersion
            ?? "unknown";

        var sourceIdsJson = JsonSerializer.Serialize(new
        {
            decisions = decisions.Select(d => d.Id).Take(limit),
            observations = observations.Select(o => o.Id).Take(limit),
            orders = orders.Select(o => o.Id).Take(limit),
            trades = trades.Select(t => t.Id).Take(limit),
            orderEvents = orderEvents.Select(e => e.Id).Take(limit),
            riskEvents = riskEvents.Select(e => e.Id).Take(limit)
        }, JsonOptions);

        var metricsJson = JsonSerializer.Serialize(new
        {
            topDecisionReasons = decisions
                .GroupBy(d => d.Reason)
                .OrderByDescending(g => g.Count())
                .Take(20)
                .Select(g => new { reason = g.Key, count = g.Count() }),
            topObservationReasons = observations
                .GroupBy(o => o.ReasonCode)
                .OrderByDescending(g => g.Count())
                .Take(20)
                .Select(g => new { reasonCode = g.Key, count = g.Count() }),
            riskEvents = riskEvents
                .GroupBy(e => e.Code)
                .OrderByDescending(g => g.Count())
                .Take(20)
                .Select(g => new { code = g.Key, count = g.Count() }),
            orderStatuses = orders
                .GroupBy(o => o.Status.ToString())
                .OrderByDescending(g => g.Count())
                .Select(g => new { status = g.Key, count = g.Count() }),
            pnl = new
            {
                pnl.GrossProfit,
                pnl.NetProfit,
                pnl.TotalFees,
                pnl.TradeCount
            }
        }, JsonOptions);

        return new StrategyEpisode(
            request.StrategyId,
            request.MarketId,
            configVersion,
            request.WindowStartUtc,
            request.WindowEndUtc,
            decisions.Count,
            observations.Count,
            orders.Count,
            trades.Count,
            riskEvents.Count,
            pnl.NetProfit,
            fillRate,
            rejectRate,
            timeoutRate,
            maxOpenExposure,
            drawdown,
            sourceIdsJson,
            metricsJson,
            DateTimeOffset.UtcNow);
    }
}
