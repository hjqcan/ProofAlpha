using System.Text.Json;
using Autotrade.Strategy.Application.Decisions;
using Autotrade.Strategy.Application.RunSessions;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Strategy.Application.RunReports;

public sealed class PaperRunReportService(
    IPaperRunSessionService sessionService,
    IStrategyDecisionQueryService decisionQueryService,
    IOrderEventRepository orderEventRepository,
    IOrderRepository orderRepository,
    ITradeRepository tradeRepository,
    IPositionRepository positionRepository,
    IRiskEventRepository riskEventRepository) : IPaperRunReportService
{
    private static readonly string[] CsvTables =
    [
        "summary",
        "strategies",
        "markets",
        "attribution",
        "unhedged_exposures",
        "risk_events",
        "incidents",
        "evidence"
    ];

    private readonly IPaperRunSessionService _sessionService =
        sessionService ?? throw new ArgumentNullException(nameof(sessionService));
    private readonly IStrategyDecisionQueryService _decisionQueryService =
        decisionQueryService ?? throw new ArgumentNullException(nameof(decisionQueryService));
    private readonly IOrderEventRepository _orderEventRepository =
        orderEventRepository ?? throw new ArgumentNullException(nameof(orderEventRepository));
    private readonly IOrderRepository _orderRepository =
        orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
    private readonly ITradeRepository _tradeRepository =
        tradeRepository ?? throw new ArgumentNullException(nameof(tradeRepository));
    private readonly IPositionRepository _positionRepository =
        positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
    private readonly IRiskEventRepository _riskEventRepository =
        riskEventRepository ?? throw new ArgumentNullException(nameof(riskEventRepository));

    public async Task<PaperRunReport?> GetAsync(
        Guid sessionId,
        int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            return null;
        }

        var normalizedLimit = Math.Clamp(limit, 1, 5000);
        var session = await _sessionService.ExportAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return null;
        }

        var toUtc = session.StoppedAtUtc ?? DateTimeOffset.UtcNow;
        var evidence = await LoadEvidenceAsync(session, toUtc, normalizedLimit, cancellationToken)
            .ConfigureAwait(false);

        var summary = BuildSummary(evidence);
        var status = DetermineStatus(session, summary);
        var notes = BuildCompletenessNotes(session, summary);
        var incidents = BuildIncidents(evidence);
        var strategyBreakdown = BuildStrategyBreakdown(session, evidence);
        var marketBreakdown = BuildMarketBreakdown(evidence);
        var attribution = BuildAttribution(summary, strategyBreakdown, marketBreakdown, evidence);

        return new PaperRunReport(
            DateTimeOffset.UtcNow,
            status,
            notes,
            session,
            summary,
            strategyBreakdown,
            marketBreakdown,
            evidence.RiskEvents,
            incidents,
            BuildEvidenceLinks(evidence),
            new PaperRunExportReferences(
                $"/api/run-reports/{session.SessionId}",
                $"autotrade export run-report --session-id {session.SessionId} --json",
                $"autotrade export run-report --session-id {session.SessionId}",
                CsvTables))
        {
            Attribution = attribution
        };
    }

    private async Task<PaperRunReportEvidence> LoadEvidenceAsync(
        PaperRunSessionRecord session,
        DateTimeOffset toUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        var decisions = await _decisionQueryService
            .QueryRecordsAsync(
                new StrategyDecisionQuery(
                    null,
                    null,
                    session.StartedAtUtc,
                    toUtc,
                    limit,
                    RunSessionId: session.SessionId),
                cancellationToken)
            .ConfigureAwait(false);

        var orderEvents = await _orderEventRepository
            .GetByRunSessionIdAsync(session.SessionId, limit, cancellationToken)
            .ConfigureAwait(false);

        var orderIds = orderEvents
            .Select(orderEvent => orderEvent.OrderId)
            .Where(orderId => orderId != Guid.Empty)
            .Distinct()
            .Take(limit)
            .ToArray();

        var orders = await LoadOrdersAsync(orderIds, cancellationToken).ConfigureAwait(false);
        var trades = await LoadTradesAsync(orderIds, limit, cancellationToken).ConfigureAwait(false);
        var positions = await LoadPositionsAsync(decisions, orderEvents, orders, trades, cancellationToken)
            .ConfigureAwait(false);

        var strategySet = session.Strategies.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var riskEvents = await _riskEventRepository
            .QueryAsync(null, session.StartedAtUtc, toUtc, limit, cancellationToken)
            .ConfigureAwait(false);
        riskEvents = riskEvents
            .Where(riskEvent => riskEvent.StrategyId is null || strategySet.Contains(riskEvent.StrategyId))
            .GroupBy(riskEvent => riskEvent.Id)
            .Select(group => group.First())
            .OrderByDescending(riskEvent => riskEvent.CreatedAtUtc)
            .Take(limit)
            .ToArray();

        return new PaperRunReportEvidence(decisions, orderEvents, orders, trades, positions, riskEvents);
    }

    private async Task<IReadOnlyList<OrderDto>> LoadOrdersAsync(
        IReadOnlyList<Guid> orderIds,
        CancellationToken cancellationToken)
    {
        var orders = new List<OrderDto>(orderIds.Count);
        foreach (var orderId in orderIds)
        {
            var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
            if (order is not null)
            {
                orders.Add(order);
            }
        }

        return orders;
    }

    private async Task<IReadOnlyList<TradeDto>> LoadTradesAsync(
        IReadOnlyList<Guid> orderIds,
        int limit,
        CancellationToken cancellationToken)
    {
        var trades = new List<TradeDto>();
        foreach (var orderId in orderIds)
        {
            var orderTrades = await _tradeRepository.GetByOrderIdAsync(orderId, cancellationToken)
                .ConfigureAwait(false);
            trades.AddRange(orderTrades);
            if (trades.Count >= limit)
            {
                break;
            }
        }

        return trades
            .GroupBy(trade => trade.Id)
            .Select(group => group.First())
            .OrderByDescending(trade => trade.CreatedAtUtc)
            .Take(limit)
            .ToArray();
    }

    private async Task<IReadOnlyList<PositionDto>> LoadPositionsAsync(
        IReadOnlyList<StrategyDecisionRecord> decisions,
        IReadOnlyList<OrderEventDto> orderEvents,
        IReadOnlyList<OrderDto> orders,
        IReadOnlyList<TradeDto> trades,
        CancellationToken cancellationToken)
    {
        var marketIds = decisions.Select(decision => decision.MarketId)
            .Concat(orderEvents.Select(orderEvent => orderEvent.MarketId))
            .Concat(orders.Select(order => order.MarketId))
            .Concat(trades.Select(trade => trade.MarketId))
            .Where(marketId => !string.IsNullOrWhiteSpace(marketId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (marketIds.Count == 0)
        {
            return Array.Empty<PositionDto>();
        }

        var positions = await _positionRepository.GetNonZeroAsync(cancellationToken).ConfigureAwait(false);
        return positions
            .Where(position => marketIds.Contains(position.MarketId))
            .OrderBy(position => position.MarketId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(position => position.Outcome)
            .ToArray();
    }

    private static PaperRunReportSummary BuildSummary(PaperRunReportEvidence evidence)
    {
        var buyNotional = evidence.Trades
            .Where(trade => trade.Side == OrderSide.Buy)
            .Sum(trade => trade.Notional);
        var sellNotional = evidence.Trades
            .Where(trade => trade.Side == OrderSide.Sell)
            .Sum(trade => trade.Notional);
        var fees = evidence.Trades.Sum(trade => trade.Fee);
        var grossPnl = sellNotional - buyNotional;

        return new PaperRunReportSummary(
            evidence.Decisions.Count,
            evidence.OrderEvents.Count,
            evidence.Orders.Count,
            evidence.Trades.Count,
            evidence.Positions.Count,
            evidence.RiskEvents.Count,
            evidence.OrderEvents.Count(orderEvent => orderEvent.EventType is OrderEventType.Filled or OrderEventType.PartiallyFilled),
            evidence.OrderEvents.Count(orderEvent => orderEvent.EventType == OrderEventType.Rejected),
            buyNotional,
            sellNotional,
            fees,
            grossPnl,
            grossPnl - fees);
    }

    private static string DetermineStatus(PaperRunSessionRecord session, PaperRunReportSummary summary)
    {
        if (summary.DecisionCount == 0
            && summary.OrderEventCount == 0
            && summary.TradeCount == 0
            && summary.RiskEventCount == 0)
        {
            return "Empty";
        }

        return session.IsActive ? "Partial" : "Complete";
    }

    private static IReadOnlyList<string> BuildCompletenessNotes(
        PaperRunSessionRecord session,
        PaperRunReportSummary summary)
    {
        var notes = new List<string>();
        if (session.IsActive)
        {
            notes.Add("Session is still active; report is partial until the run is stopped.");
        }

        if (summary.DecisionCount == 0)
        {
            notes.Add("No decisions were recorded for this run session.");
        }

        if (summary.OrderEventCount == 0)
        {
            notes.Add("No order audit events were recorded for this run session.");
        }

        if (summary.TradeCount == 0)
        {
            notes.Add("No trades were linked through session order audit events.");
        }

        if (summary.RiskEventCount == 0)
        {
            notes.Add("No risk events were recorded in the session time window.");
        }

        return notes;
    }

    private static IReadOnlyList<PaperRunStrategyBreakdown> BuildStrategyBreakdown(
        PaperRunSessionRecord session,
        PaperRunReportEvidence evidence)
    {
        var slippage = BuildSlippageEstimates(evidence);
        var latency = BuildLatencyEstimates(evidence);
        var unhedged = BuildUnhedgedExposureRecords(evidence.RiskEvents);

        var strategyIds = session.Strategies
            .Concat(evidence.Decisions.Select(decision => decision.StrategyId))
            .Concat(evidence.OrderEvents.Select(orderEvent => orderEvent.StrategyId))
            .Concat(evidence.Orders.Select(order => order.StrategyId))
            .Concat(evidence.Trades.Select(trade => trade.StrategyId))
            .Concat(evidence.RiskEvents.Select(riskEvent => riskEvent.StrategyId))
            .Where(strategyId => !string.IsNullOrWhiteSpace(strategyId))
            .Select(strategyId => strategyId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(strategyId => strategyId, StringComparer.OrdinalIgnoreCase);

        return strategyIds
            .Select(strategyId =>
            {
                var trades = evidence.Trades
                    .Where(trade => string.Equals(trade.StrategyId, strategyId, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                var buyNotional = trades.Where(trade => trade.Side == OrderSide.Buy).Sum(trade => trade.Notional);
                var sellNotional = trades.Where(trade => trade.Side == OrderSide.Sell).Sum(trade => trade.Notional);
                var fees = trades.Sum(trade => trade.Fee);
                var strategySlippage = slippage
                    .Where(item => IsSame(item.StrategyId, strategyId))
                    .Sum(item => item.SignedSlippage);
                var strategyLatency = latency
                    .Where(item => IsSame(item.StrategyId, strategyId))
                    .Select(item => item.DecisionToFillLatencyMs)
                    .Where(value => value.HasValue)
                    .Select(value => value!.Value)
                    .ToArray();
                var strategyUnhedged = unhedged
                    .Where(item => IsSame(item.StrategyId, strategyId))
                    .ToArray();

                return new PaperRunStrategyBreakdown(
                    strategyId,
                    evidence.Decisions.Count(decision => string.Equals(decision.StrategyId, strategyId, StringComparison.OrdinalIgnoreCase)),
                    evidence.OrderEvents.Count(orderEvent => string.Equals(orderEvent.StrategyId, strategyId, StringComparison.OrdinalIgnoreCase)),
                    evidence.Orders.Count(order => string.Equals(order.StrategyId, strategyId, StringComparison.OrdinalIgnoreCase)),
                    trades.Length,
                    evidence.RiskEvents.Count(riskEvent => string.Equals(riskEvent.StrategyId, strategyId, StringComparison.OrdinalIgnoreCase)),
                    buyNotional,
                    sellNotional,
                    fees,
                    sellNotional - buyNotional - fees)
                {
                    EstimatedSlippage = strategySlippage,
                    AverageDecisionToFillLatencyMs = Average(strategyLatency),
                    StaleDataEventCount = evidence.RiskEvents.Count(riskEvent =>
                        IsSame(riskEvent.StrategyId, strategyId) && IsStaleRiskEvent(riskEvent)),
                    UnhedgedExposureNotional = strategyUnhedged.Sum(item => item.Notional),
                    UnhedgedExposureSeconds = strategyUnhedged.Sum(item => item.DurationSeconds)
                };
            })
            .ToArray();
    }

    private static IReadOnlyList<PaperRunMarketBreakdown> BuildMarketBreakdown(PaperRunReportEvidence evidence)
    {
        var slippage = BuildSlippageEstimates(evidence);
        var latency = BuildLatencyEstimates(evidence);
        var unhedged = BuildUnhedgedExposureRecords(evidence.RiskEvents);

        var marketIds = evidence.Decisions.Select(decision => decision.MarketId)
            .Concat(evidence.OrderEvents.Select(orderEvent => orderEvent.MarketId))
            .Concat(evidence.Orders.Select(order => order.MarketId))
            .Concat(evidence.Trades.Select(trade => trade.MarketId))
            .Concat(evidence.Positions.Select(position => position.MarketId))
            .Where(marketId => !string.IsNullOrWhiteSpace(marketId))
            .Select(marketId => marketId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(marketId => marketId, StringComparer.OrdinalIgnoreCase);

        return marketIds
            .Select(marketId =>
            {
                var trades = evidence.Trades
                    .Where(trade => string.Equals(trade.MarketId, marketId, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                var buyNotional = trades.Where(trade => trade.Side == OrderSide.Buy).Sum(trade => trade.Notional);
                var sellNotional = trades.Where(trade => trade.Side == OrderSide.Sell).Sum(trade => trade.Notional);
                var fees = trades.Sum(trade => trade.Fee);
                var marketSlippage = slippage
                    .Where(item => IsSame(item.MarketId, marketId))
                    .Sum(item => item.SignedSlippage);
                var marketLatency = latency
                    .Where(item => IsSame(item.MarketId, marketId))
                    .Select(item => item.DecisionToFillLatencyMs)
                    .Where(value => value.HasValue)
                    .Select(value => value!.Value)
                    .ToArray();
                var marketUnhedged = unhedged
                    .Where(item => IsSame(item.MarketId, marketId))
                    .ToArray();

                return new PaperRunMarketBreakdown(
                    marketId,
                    evidence.Decisions.Count(decision => string.Equals(decision.MarketId, marketId, StringComparison.OrdinalIgnoreCase)),
                    evidence.OrderEvents.Count(orderEvent => string.Equals(orderEvent.MarketId, marketId, StringComparison.OrdinalIgnoreCase)),
                    evidence.Orders.Count(order => string.Equals(order.MarketId, marketId, StringComparison.OrdinalIgnoreCase)),
                    trades.Length,
                    evidence.Positions.Count(position => string.Equals(position.MarketId, marketId, StringComparison.OrdinalIgnoreCase)),
                    buyNotional,
                    sellNotional,
                    sellNotional - buyNotional - fees)
                {
                    EstimatedSlippage = marketSlippage,
                    AverageDecisionToFillLatencyMs = Average(marketLatency),
                    StaleDataEventCount = evidence.RiskEvents.Count(riskEvent =>
                        IsSame(TryGetContextString(riskEvent.ContextJson, "MarketId"), marketId) && IsStaleRiskEvent(riskEvent)),
                    UnhedgedExposureNotional = marketUnhedged.Sum(item => item.Notional),
                    UnhedgedExposureSeconds = marketUnhedged.Sum(item => item.DurationSeconds)
                };
            })
            .ToArray();
    }

    private static PaperRunAttribution BuildAttribution(
        PaperRunReportSummary summary,
        IReadOnlyList<PaperRunStrategyBreakdown> strategyBreakdown,
        IReadOnlyList<PaperRunMarketBreakdown> marketBreakdown,
        PaperRunReportEvidence evidence)
    {
        var slippage = BuildSlippageAttribution(evidence);
        var latency = BuildLatencyAttribution(evidence);
        var staleData = BuildStaleDataAttribution(evidence.RiskEvents);
        var unhedgedExposure = BuildUnhedgedExposureAttribution(evidence.RiskEvents);
        var strategyNetPnl = strategyBreakdown.Sum(strategy => strategy.NetPnl);
        var marketNetPnl = marketBreakdown.Sum(market => market.NetPnl);
        var strategyTotalsReconcile = strategyNetPnl == summary.NetPnl;
        var marketTotalsReconcile = marketNetPnl == summary.NetPnl;
        var notes = new List<string>();

        if (strategyTotalsReconcile)
        {
            notes.Add("Strategy net PnL reconciles to run net PnL.");
        }
        else
        {
            notes.Add($"Strategy net PnL {strategyNetPnl} does not equal run net PnL {summary.NetPnl}; evidence may be truncated or missing strategy IDs.");
        }

        if (marketTotalsReconcile)
        {
            notes.Add("Market net PnL reconciles to run net PnL.");
        }
        else
        {
            notes.Add($"Market net PnL {marketNetPnl} does not equal run net PnL {summary.NetPnl}; evidence may be truncated or missing market IDs.");
        }

        return new PaperRunAttribution(
            BuildPnlAttribution(summary, evidence),
            slippage,
            latency,
            staleData,
            unhedgedExposure,
            strategyTotalsReconcile,
            marketTotalsReconcile,
            notes);
    }

    private static PaperRunPnlAttribution BuildPnlAttribution(
        PaperRunReportSummary summary,
        PaperRunReportEvidence evidence)
    {
        var notes = new List<string>();
        var realizedPnlSource = "trade_cashflow_net_pnl";
        var realizedPnl = summary.NetPnl;

        if (evidence.Positions.Count > 0)
        {
            realizedPnl = evidence.Positions.Sum(position => position.RealizedPnl);
            realizedPnlSource = "position_realized_pnl";
            if (realizedPnl != summary.NetPnl)
            {
                notes.Add($"Position realized PnL {realizedPnl} differs from trade cash-flow net PnL {summary.NetPnl}.");
            }
        }

        notes.Add("Unrealized PnL is omitted because report evidence does not contain per-position current mark prices.");

        return new PaperRunPnlAttribution(
            realizedPnl,
            UnrealizedPnl: null,
            summary.TotalFees,
            summary.GrossPnl,
            summary.NetPnl,
            realizedPnlSource,
            "unavailable_without_mark_price",
            "trade_fee",
            "unavailable",
            notes);
    }

    private static PaperRunSlippageAttribution BuildSlippageAttribution(PaperRunReportEvidence evidence)
    {
        var estimates = BuildSlippageEstimates(evidence);
        var estimatedTradeIds = estimates.Select(item => item.TradeId).ToHashSet();
        var missing = evidence.Trades.Count(trade => !estimatedTradeIds.Contains(trade.Id));
        var notes = new List<string>();
        if (missing > 0)
        {
            notes.Add($"{missing} trade(s) could not be matched to an order limit price for slippage estimation.");
        }

        notes.Add("Slippage is estimated as fill price versus submitted order limit price; it is not a midpoint or arrival-price model.");

        return new PaperRunSlippageAttribution(
            estimates.Sum(item => item.SignedSlippage),
            estimates.Where(item => item.SignedSlippage > 0m).Sum(item => item.SignedSlippage),
            Math.Abs(estimates.Where(item => item.SignedSlippage < 0m).Sum(item => item.SignedSlippage)),
            "order_limit_price_vs_fill_price",
            estimates.Count,
            missing,
            estimates.Select(item => item.TradeId).ToArray(),
            notes);
    }

    private static PaperRunLatencyAttribution BuildLatencyAttribution(PaperRunReportEvidence evidence)
    {
        var estimates = BuildLatencyEstimates(evidence);
        var decisionLatencies = estimates
            .Select(item => item.DecisionToFillLatencyMs)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        var acceptedLatencies = estimates
            .Select(item => item.AcceptedToFillLatencyMs)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        var notes = new List<string>();
        var fillCount = evidence.OrderEvents.Count(orderEvent => IsFillEvent(orderEvent.EventType));
        if (decisionLatencies.Length < fillCount)
        {
            notes.Add($"{fillCount - decisionLatencies.Length} fill event(s) had no prior strategy decision for latency attribution.");
        }

        if (acceptedLatencies.Length < fillCount)
        {
            notes.Add($"{fillCount - acceptedLatencies.Length} fill event(s) had no accepted/submitted order event for execution latency attribution.");
        }

        return new PaperRunLatencyAttribution(
            Average(decisionLatencies),
            Percentile(decisionLatencies, 0.95d),
            Average(acceptedLatencies),
            decisionLatencies.Length,
            acceptedLatencies.Length,
            estimates.Select(item => item.FillEventId).ToArray(),
            notes);
    }

    private static PaperRunStaleDataAttribution BuildStaleDataAttribution(IReadOnlyList<RiskEventRecord> riskEvents)
    {
        var staleEvents = riskEvents
            .Where(IsStaleRiskEvent)
            .ToArray();
        var notes = new List<string>();
        if (staleEvents.Length == 0)
        {
            notes.Add("No stale-data risk events were recorded in the run window.");
        }
        else
        {
            notes.Add("Stale-data contribution is event-based because decision evidence does not persist the stale quote delta needed for PnL decomposition.");
        }

        return new PaperRunStaleDataAttribution(
            staleEvents.Length,
            EstimatedPnlContribution: null,
            "risk_events",
            staleEvents.Select(riskEvent => riskEvent.Id).ToArray(),
            notes);
    }

    private static PaperRunUnhedgedExposureAttribution BuildUnhedgedExposureAttribution(
        IReadOnlyList<RiskEventRecord> riskEvents)
    {
        var exposures = BuildUnhedgedExposureRecords(riskEvents);
        var notes = new List<string>();
        if (exposures.Count == 0)
        {
            notes.Add("No unhedged exposure timeout events were recorded in the run window.");
        }

        return new PaperRunUnhedgedExposureAttribution(
            exposures.Count,
            exposures.Sum(exposure => exposure.Notional),
            exposures.Sum(exposure => exposure.DurationSeconds),
            Average(exposures.Select(exposure => exposure.DurationSeconds).ToArray()),
            exposures,
            notes);
    }

    private static IReadOnlyList<PaperRunSlippageEstimate> BuildSlippageEstimates(PaperRunReportEvidence evidence)
    {
        var ordersById = evidence.Orders
            .GroupBy(order => order.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var estimates = new List<PaperRunSlippageEstimate>();
        foreach (var trade in evidence.Trades)
        {
            if (!ordersById.TryGetValue(trade.OrderId, out var order))
            {
                continue;
            }

            var signedSlippage = trade.Side == OrderSide.Buy
                ? (trade.Price - order.Price) * trade.Quantity
                : (order.Price - trade.Price) * trade.Quantity;
            estimates.Add(new PaperRunSlippageEstimate(
                trade.Id,
                trade.StrategyId,
                trade.MarketId,
                signedSlippage));
        }

        return estimates;
    }

    private static IReadOnlyList<PaperRunLatencyEstimate> BuildLatencyEstimates(PaperRunReportEvidence evidence)
    {
        var eventsByOrderId = evidence.OrderEvents
            .GroupBy(orderEvent => orderEvent.OrderId)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.CreatedAtUtc).ToArray());
        var estimates = new List<PaperRunLatencyEstimate>();

        foreach (var fill in evidence.OrderEvents.Where(orderEvent => IsFillEvent(orderEvent.EventType)))
        {
            var priorDecision = evidence.Decisions
                .Where(decision => IsSame(decision.StrategyId, fill.StrategyId)
                    && IsSame(decision.MarketId, fill.MarketId)
                    && decision.TimestampUtc <= fill.CreatedAtUtc)
                .OrderByDescending(decision => decision.TimestampUtc)
                .FirstOrDefault();
            var acceptedEvent = eventsByOrderId.TryGetValue(fill.OrderId, out var orderEvents)
                ? orderEvents
                    .Where(orderEvent => orderEvent.CreatedAtUtc <= fill.CreatedAtUtc
                        && orderEvent.EventType is OrderEventType.Accepted or OrderEventType.Submitted or OrderEventType.Created)
                    .OrderByDescending(orderEvent => orderEvent.CreatedAtUtc)
                    .FirstOrDefault()
                : null;

            estimates.Add(new PaperRunLatencyEstimate(
                fill.Id,
                fill.StrategyId,
                fill.MarketId,
                priorDecision is null ? null : (fill.CreatedAtUtc - priorDecision.TimestampUtc).TotalMilliseconds,
                acceptedEvent is null ? null : (fill.CreatedAtUtc - acceptedEvent.CreatedAtUtc).TotalMilliseconds));
        }

        return estimates;
    }

    private static IReadOnlyList<PaperRunUnhedgedExposureRecord> BuildUnhedgedExposureRecords(
        IReadOnlyList<RiskEventRecord> riskEvents)
        => riskEvents
            .Where(IsUnhedgedExposureEvent)
            .Select(ToUnhedgedExposureRecord)
            .ToArray();

    private static PaperRunUnhedgedExposureRecord ToUnhedgedExposureRecord(RiskEventRecord riskEvent)
    {
        var startedAt = TryGetContextDateTimeOffset(riskEvent.ContextJson, "StartedAtUtc");
        var endedAt = TryGetContextDateTimeOffset(riskEvent.ContextJson, "ExpiredAtUtc") ?? riskEvent.CreatedAtUtc;
        var duration = TryGetContextDouble(riskEvent.ContextJson, "ExposureDurationSeconds")
            ?? (startedAt.HasValue ? Math.Max(0d, (endedAt - startedAt.Value).TotalSeconds) : 0d);

        return new PaperRunUnhedgedExposureRecord(
            riskEvent.Id,
            riskEvent.StrategyId ?? TryGetContextString(riskEvent.ContextJson, "StrategyId"),
            TryGetContextString(riskEvent.ContextJson, "MarketId"),
            TryGetContextDecimal(riskEvent.ContextJson, "Notional") ?? 0m,
            duration,
            TryGetContextString(riskEvent.ContextJson, "ExitAction") ?? DeriveUnhedgedMitigationOutcome(riskEvent.Code),
            startedAt,
            endedAt);
    }

    private static bool IsFillEvent(OrderEventType eventType)
        => eventType is OrderEventType.Filled or OrderEventType.PartiallyFilled;

    private static bool IsStaleRiskEvent(RiskEventRecord riskEvent)
        => ContainsIgnoreCase(riskEvent.Code, "STALE") || ContainsIgnoreCase(riskEvent.Message, "STALE");

    private static bool IsUnhedgedExposureEvent(RiskEventRecord riskEvent)
        => ContainsIgnoreCase(riskEvent.Code, "UNHEDGED") || ContainsIgnoreCase(riskEvent.Message, "unhedged");

    private static bool ContainsIgnoreCase(string value, string token)
        => value.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static bool IsSame(string? left, string? right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static double? Average(IReadOnlyList<double> values)
        => values.Count == 0 ? null : values.Average();

    private static double? Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var sorted = values.Order().ToArray();
        var index = Math.Clamp((int)Math.Ceiling(sorted.Length * percentile) - 1, 0, sorted.Length - 1);
        return sorted[index];
    }

    private static string DeriveUnhedgedMitigationOutcome(string code)
    {
        if (ContainsIgnoreCase(code, "FORCE_HEDGE"))
        {
            return "ForceHedge";
        }

        if (ContainsIgnoreCase(code, "EXIT"))
        {
            return "CancelAndExit";
        }

        if (ContainsIgnoreCase(code, "CANCEL"))
        {
            return "CancelOrders";
        }

        return ContainsIgnoreCase(code, "LOG") ? "LogOnly" : "Unknown";
    }

    private static string? TryGetContextString(string? contextJson, string propertyName)
    {
        using var document = TryParseJson(contextJson);
        if (document is null)
        {
            return null;
        }

        return TryGetProperty(document.RootElement, propertyName, out var property)
            ? property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString()
            : null;
    }

    private static decimal? TryGetContextDecimal(string? contextJson, string propertyName)
    {
        using var document = TryParseJson(contextJson);
        if (document is null || !TryGetProperty(document.RootElement, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetDecimal(out var value) => value,
            JsonValueKind.String when decimal.TryParse(property.GetString(), out var value) => value,
            _ => null
        };
    }

    private static double? TryGetContextDouble(string? contextJson, string propertyName)
    {
        using var document = TryParseJson(contextJson);
        if (document is null || !TryGetProperty(document.RootElement, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetDouble(out var value) => value,
            JsonValueKind.String when double.TryParse(property.GetString(), out var value) => value,
            _ => null
        };
    }

    private static DateTimeOffset? TryGetContextDateTimeOffset(string? contextJson, string propertyName)
    {
        var value = TryGetContextString(contextJson, propertyName);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static JsonDocument? TryParseJson(string? contextJson)
    {
        if (string.IsNullOrWhiteSpace(contextJson))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(contextJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
    {
        foreach (var candidate in element.EnumerateObject())
        {
            if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }

    private static IReadOnlyList<PaperRunIncident> BuildIncidents(PaperRunReportEvidence evidence)
    {
        var riskIncidents = evidence.RiskEvents
            .Where(riskEvent => riskEvent.Severity != RiskSeverity.Info)
            .Select(riskEvent => new PaperRunIncident(
                riskEvent.CreatedAtUtc,
                "RiskEvent",
                riskEvent.Severity.ToString(),
                riskEvent.Code,
                riskEvent.Message,
                riskEvent.StrategyId,
                null,
                riskEvent.Id));

        var orderIncidents = evidence.OrderEvents
            .Where(orderEvent => orderEvent.EventType is OrderEventType.Rejected
                or OrderEventType.Cancelled
                or OrderEventType.Expired)
            .Select(orderEvent => new PaperRunIncident(
                orderEvent.CreatedAtUtc,
                "OrderEvent",
                "Warning",
                orderEvent.EventType.ToString(),
                orderEvent.Message,
                orderEvent.StrategyId,
                orderEvent.MarketId,
                orderEvent.Id));

        return riskIncidents
            .Concat(orderIncidents)
            .OrderByDescending(incident => incident.TimestampUtc)
            .ToArray();
    }

    private static PaperRunEvidenceLinks BuildEvidenceLinks(PaperRunReportEvidence evidence)
        => new(
            evidence.Decisions.Select(decision => decision.DecisionId).Distinct().ToArray(),
            evidence.OrderEvents.Select(orderEvent => orderEvent.Id).Distinct().ToArray(),
            evidence.Orders.Select(order => order.Id).Distinct().ToArray(),
            evidence.Trades.Select(trade => trade.Id).Distinct().ToArray(),
            evidence.Positions.Select(position => position.Id).Distinct().ToArray(),
            evidence.RiskEvents.Select(riskEvent => riskEvent.Id).Distinct().ToArray());

    private sealed record PaperRunSlippageEstimate(
        Guid TradeId,
        string StrategyId,
        string MarketId,
        decimal SignedSlippage);

    private sealed record PaperRunLatencyEstimate(
        Guid FillEventId,
        string StrategyId,
        string MarketId,
        double? DecisionToFillLatencyMs,
        double? AcceptedToFillLatencyMs);
}
