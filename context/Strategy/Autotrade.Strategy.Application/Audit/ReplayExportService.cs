using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Autotrade.Application.Readiness;
using Autotrade.Strategy.Application.Decisions;
using Autotrade.Strategy.Application.RunSessions;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.Contract.Risk;

namespace Autotrade.Strategy.Application.Audit;

public sealed class ReplayExportService(
    IAuditTimelineService auditTimelineService,
    IStrategyDecisionQueryService decisionQueryService,
    IOrderEventRepository orderEventRepository,
    IOrderRepository orderRepository,
    ITradeRepository tradeRepository,
    IPositionRepository positionRepository,
    IRiskEventRepository riskEventRepository,
    IPaperRunSessionService runSessionService,
    IReadinessReportService readinessReportService) : IReplayExportService
{
    public const string ContractVersion = "autotrade.replay-export.v1";

    private const int MaxLimit = 5000;

    private static readonly string[] RedactionRules =
    [
        "JSON property names containing password, secret, privateKey, apiKey, authorization, credential, signature, passphrase, or mnemonic are replaced with [redacted].",
        "Raw text fragments that look like key, secret, password, authorization, credential, signature, passphrase, or mnemonic assignments are redacted.",
        "TradingAccountId, OrderSalt, and OrderTimestamp are excluded from order, trade, and position evidence records."
    ];

    private static readonly string[] ExcludedFields =
    [
        "TradingAccountId",
        "OrderSalt",
        "OrderTimestamp"
    ];

    private static readonly Regex RawSecretRegex = new(
        @"(?i)((?:api[_-]?key|private[_-]?key|password|secret|authorization|credential|signature|passphrase|mnemonic)\s*[:=]\s*)[^,\s}]+",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    public async Task<ReplayExportPackage> ExportAsync(
        ReplayExportQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var normalizedQuery = query with { Limit = Math.Clamp(query.Limit, 1, MaxLimit) };
        var runSession = await LoadRunSessionAsync(normalizedQuery, cancellationToken).ConfigureAwait(false);
        var effectiveFrom = normalizedQuery.FromUtc ?? runSession?.StartedAtUtc;
        var effectiveTo = normalizedQuery.ToUtc ?? runSession?.StoppedAtUtc;

        var timelineTask = auditTimelineService.QueryAsync(ToTimelineQuery(normalizedQuery), cancellationToken);
        var decisionsTask = LoadDecisionsAsync(normalizedQuery, effectiveFrom, effectiveTo, cancellationToken);
        var orderEventsTask = LoadOrderEventsAsync(normalizedQuery, effectiveFrom, effectiveTo, cancellationToken);
        var riskEventsTask = LoadRiskEventsAsync(normalizedQuery, effectiveFrom, effectiveTo, cancellationToken);
        var readinessTask = LoadReadinessAsync(cancellationToken);

        await Task.WhenAll(timelineTask, decisionsTask, orderEventsTask, riskEventsTask, readinessTask)
            .ConfigureAwait(false);

        var timeline = SanitizeTimeline(await timelineTask.ConfigureAwait(false));
        var decisions = (await decisionsTask.ConfigureAwait(false)).ToArray();
        var orderEvents = (await orderEventsTask.ConfigureAwait(false)).ToArray();
        var orders = await LoadOrdersAsync(normalizedQuery, orderEvents, effectiveFrom, effectiveTo, cancellationToken)
            .ConfigureAwait(false);
        var trades = await LoadTradesAsync(normalizedQuery, orders, orderEvents, effectiveFrom, effectiveTo, cancellationToken)
            .ConfigureAwait(false);
        var positions = await LoadPositionsAsync(normalizedQuery, decisions, orderEvents, orders, trades, cancellationToken)
            .ConfigureAwait(false);
        var riskEvents = (await riskEventsTask.ConfigureAwait(false)).ToArray();
        var (readiness, readinessNote) = await readinessTask.ConfigureAwait(false);

        var notes = BuildCompletenessNotes(
            normalizedQuery,
            runSession,
            readinessNote,
            decisions,
            orderEvents,
            orders,
            trades,
            positions,
            riskEvents);

        return new ReplayExportPackage(
            DateTimeOffset.UtcNow,
            ContractVersion,
            normalizedQuery,
            new ReplayRedactionSummary(RedactionRules, ExcludedFields),
            notes,
            runSession is null ? null : ToReplayRunSession(runSession),
            timeline,
            new ReplayEvidenceBundle(
                decisions.Select(ToReplayDecision).ToArray(),
                orderEvents.Select(ToReplayOrderEvent).ToArray(),
                orders.Select(ToReplayOrder).ToArray(),
                trades.Select(ToReplayTrade).ToArray(),
                positions.Select(ToReplayPosition).ToArray(),
                riskEvents.Select(ToReplayRiskEvent).ToArray()),
            BuildStrategyConfigVersions(runSession, decisions),
            readiness,
            BuildExportReferences(normalizedQuery));
    }

    private async Task<PaperRunSessionRecord?> LoadRunSessionAsync(
        ReplayExportQuery query,
        CancellationToken cancellationToken)
    {
        if (!query.RunSessionId.HasValue || query.RunSessionId == Guid.Empty)
        {
            return null;
        }

        return await runSessionService.ExportAsync(query.RunSessionId.Value, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<StrategyDecisionRecord>> LoadDecisionsAsync(
        ReplayExportQuery query,
        DateTimeOffset? effectiveFrom,
        DateTimeOffset? effectiveTo,
        CancellationToken cancellationToken)
    {
        var decisions = await decisionQueryService
            .QueryRecordsAsync(
                new StrategyDecisionQuery(
                    query.StrategyId,
                    query.MarketId,
                    effectiveFrom,
                    effectiveTo,
                    query.Limit,
                    CorrelationId: query.CorrelationId,
                    RunSessionId: query.RunSessionId),
                cancellationToken)
            .ConfigureAwait(false);

        return decisions
            .Where(decision => MatchesDecision(decision, query))
            .OrderByDescending(decision => decision.TimestampUtc)
            .Take(query.Limit)
            .ToArray();
    }

    private async Task<IReadOnlyList<OrderEventDto>> LoadOrderEventsAsync(
        ReplayExportQuery query,
        DateTimeOffset? effectiveFrom,
        DateTimeOffset? effectiveTo,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<OrderEventDto> events;
        if (query.OrderId.HasValue && query.OrderId != Guid.Empty)
        {
            events = await orderEventRepository.GetByOrderIdAsync(query.OrderId.Value, cancellationToken)
                .ConfigureAwait(false);
        }
        else if (!string.IsNullOrWhiteSpace(query.ClientOrderId))
        {
            events = await orderEventRepository.GetByClientOrderIdAsync(query.ClientOrderId, cancellationToken)
                .ConfigureAwait(false);
        }
        else if (query.RunSessionId.HasValue && query.RunSessionId != Guid.Empty)
        {
            events = await orderEventRepository.GetByRunSessionIdAsync(query.RunSessionId.Value, query.Limit, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var page = await orderEventRepository
                .GetPagedAsync(
                    1,
                    query.Limit,
                    query.StrategyId,
                    query.MarketId,
                    null,
                    effectiveFrom,
                    effectiveTo,
                    cancellationToken)
                .ConfigureAwait(false);
            events = page.Items;
        }

        return events
            .Where(orderEvent => MatchesOrderEvent(orderEvent, query, effectiveFrom, effectiveTo))
            .GroupBy(orderEvent => orderEvent.Id)
            .Select(group => group.First())
            .OrderByDescending(orderEvent => orderEvent.CreatedAtUtc)
            .Take(query.Limit)
            .ToArray();
    }

    private async Task<IReadOnlyList<OrderDto>> LoadOrdersAsync(
        ReplayExportQuery query,
        IReadOnlyList<OrderEventDto> orderEvents,
        DateTimeOffset? effectiveFrom,
        DateTimeOffset? effectiveTo,
        CancellationToken cancellationToken)
    {
        var orderIds = orderEvents
            .Select(orderEvent => orderEvent.OrderId)
            .Concat(query.OrderId.HasValue && query.OrderId != Guid.Empty ? [query.OrderId.Value] : [])
            .Where(orderId => orderId != Guid.Empty)
            .Distinct()
            .Take(query.Limit)
            .ToArray();

        var orders = new List<OrderDto>();
        foreach (var orderId in orderIds)
        {
            var order = await orderRepository.GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
            if (order is not null)
            {
                orders.Add(order);
            }
        }

        if (!string.IsNullOrWhiteSpace(query.ClientOrderId)
            && orders.All(order => !string.Equals(order.ClientOrderId, query.ClientOrderId, StringComparison.OrdinalIgnoreCase)))
        {
            var byClientOrderId = await orderRepository
                .GetByClientOrderIdAsync(query.ClientOrderId, cancellationToken)
                .ConfigureAwait(false);
            if (byClientOrderId is not null)
            {
                orders.Add(byClientOrderId);
            }
        }

        if (orders.Count == 0 && orderIds.Length == 0)
        {
            var page = await orderRepository
                .GetPagedAsync(
                    1,
                    query.Limit,
                    query.StrategyId,
                    query.MarketId,
                    null,
                    effectiveFrom,
                    effectiveTo,
                    cancellationToken)
                .ConfigureAwait(false);
            orders.AddRange(page.Items);
        }

        return orders
            .Where(order => MatchesOrder(order, query, effectiveFrom, effectiveTo))
            .GroupBy(order => order.Id)
            .Select(group => group.First())
            .OrderByDescending(order => order.CreatedAtUtc)
            .Take(query.Limit)
            .ToArray();
    }

    private async Task<IReadOnlyList<TradeDto>> LoadTradesAsync(
        ReplayExportQuery query,
        IReadOnlyList<OrderDto> orders,
        IReadOnlyList<OrderEventDto> orderEvents,
        DateTimeOffset? effectiveFrom,
        DateTimeOffset? effectiveTo,
        CancellationToken cancellationToken)
    {
        var orderIds = orders
            .Select(order => order.Id)
            .Concat(orderEvents.Select(orderEvent => orderEvent.OrderId))
            .Concat(query.OrderId.HasValue && query.OrderId != Guid.Empty ? [query.OrderId.Value] : [])
            .Where(orderId => orderId != Guid.Empty)
            .Distinct()
            .Take(query.Limit)
            .ToArray();

        var trades = new List<TradeDto>();
        foreach (var orderId in orderIds)
        {
            trades.AddRange(await tradeRepository.GetByOrderIdAsync(orderId, cancellationToken)
                .ConfigureAwait(false));
        }

        if (!string.IsNullOrWhiteSpace(query.ClientOrderId))
        {
            trades.AddRange(await tradeRepository.GetByClientOrderIdAsync(query.ClientOrderId, cancellationToken)
                .ConfigureAwait(false));
        }

        if (trades.Count == 0 && orderIds.Length == 0)
        {
            var page = await tradeRepository
                .GetPagedAsync(
                    1,
                    query.Limit,
                    query.StrategyId,
                    query.MarketId,
                    effectiveFrom,
                    effectiveTo,
                    cancellationToken)
                .ConfigureAwait(false);
            trades.AddRange(page.Items);
        }

        return trades
            .Where(trade => MatchesTrade(trade, query, effectiveFrom, effectiveTo))
            .GroupBy(trade => trade.Id)
            .Select(group => group.First())
            .OrderByDescending(trade => trade.CreatedAtUtc)
            .Take(query.Limit)
            .ToArray();
    }

    private async Task<IReadOnlyList<PositionDto>> LoadPositionsAsync(
        ReplayExportQuery query,
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

        if (!string.IsNullOrWhiteSpace(query.MarketId))
        {
            marketIds.Add(query.MarketId);
        }

        if (marketIds.Count == 0 && HasEvidenceScope(query))
        {
            return Array.Empty<PositionDto>();
        }

        var positions = await positionRepository.GetNonZeroAsync(cancellationToken).ConfigureAwait(false);
        return positions
            .Where(position => marketIds.Count == 0 || marketIds.Contains(position.MarketId))
            .OrderBy(position => position.MarketId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(position => position.Outcome)
            .Take(query.Limit)
            .ToArray();
    }

    private async Task<IReadOnlyList<RiskEventRecord>> LoadRiskEventsAsync(
        ReplayExportQuery query,
        DateTimeOffset? effectiveFrom,
        DateTimeOffset? effectiveTo,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RiskEventRecord> riskEvents;
        if (query.RiskEventId.HasValue && query.RiskEventId != Guid.Empty)
        {
            var riskEvent = await riskEventRepository.GetAsync(query.RiskEventId.Value, cancellationToken)
                .ConfigureAwait(false);
            riskEvents = riskEvent is null ? [] : [riskEvent];
        }
        else
        {
            riskEvents = await riskEventRepository
                .QueryAsync(query.StrategyId, effectiveFrom, effectiveTo, query.Limit, cancellationToken)
                .ConfigureAwait(false);
        }

        return riskEvents
            .Where(riskEvent => MatchesRiskEvent(riskEvent, query, effectiveFrom, effectiveTo))
            .GroupBy(riskEvent => riskEvent.Id)
            .Select(group => group.First())
            .OrderByDescending(riskEvent => riskEvent.CreatedAtUtc)
            .Take(query.Limit)
            .ToArray();
    }

    private async Task<(ReplayReadinessReport? Report, string? Note)> LoadReadinessAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var report = await readinessReportService.GetReportAsync(cancellationToken).ConfigureAwait(false);
            return (ToReplayReadinessReport(report), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return (null, $"Readiness state was unavailable: {exception.Message}");
        }
    }

    private static AuditTimelineQuery ToTimelineQuery(ReplayExportQuery query)
        => new(
            query.StrategyId,
            query.MarketId,
            query.OrderId,
            query.ClientOrderId,
            query.RunSessionId,
            query.RiskEventId,
            query.CorrelationId,
            query.FromUtc,
            query.ToUtc,
            query.Limit);

    private static AuditTimeline SanitizeTimeline(AuditTimeline timeline)
        => timeline with
        {
            Items = timeline.Items
                .Select(item => item with { DetailJson = SanitizeJson(item.DetailJson) })
                .ToArray()
        };

    private static bool MatchesDecision(StrategyDecisionRecord decision, ReplayExportQuery query)
        => MatchesText(query.StrategyId, decision.StrategyId)
            && MatchesText(query.MarketId, decision.MarketId, decision.ContextJson, "marketId", "MarketId")
            && MatchesGuid(query.OrderId, null, decision.ContextJson, "orderId", "OrderId")
            && MatchesText(query.ClientOrderId, null, decision.ContextJson, "clientOrderId", "ClientOrderId")
            && MatchesGuid(query.RunSessionId, decision.RunSessionId, decision.ContextJson, "runSessionId", "RunSessionId", "sessionId", "SessionId")
            && MatchesGuid(query.RiskEventId, null, decision.ContextJson, "riskEventId", "RiskEventId")
            && MatchesText(query.CorrelationId, decision.CorrelationId, decision.ContextJson, "correlationId", "CorrelationId");

    private static bool HasEvidenceScope(ReplayExportQuery query)
        => !string.IsNullOrWhiteSpace(query.StrategyId)
            || !string.IsNullOrWhiteSpace(query.MarketId)
            || (query.OrderId.HasValue && query.OrderId != Guid.Empty)
            || !string.IsNullOrWhiteSpace(query.ClientOrderId)
            || (query.RunSessionId.HasValue && query.RunSessionId != Guid.Empty)
            || (query.RiskEventId.HasValue && query.RiskEventId != Guid.Empty)
            || !string.IsNullOrWhiteSpace(query.CorrelationId);

    private static bool MatchesOrderEvent(
        OrderEventDto orderEvent,
        ReplayExportQuery query,
        DateTimeOffset? effectiveFrom,
        DateTimeOffset? effectiveTo)
        => MatchesTime(orderEvent.CreatedAtUtc, effectiveFrom, effectiveTo)
            && MatchesText(query.StrategyId, orderEvent.StrategyId)
            && MatchesText(query.MarketId, orderEvent.MarketId)
            && MatchesGuid(query.OrderId, orderEvent.OrderId)
            && MatchesText(query.ClientOrderId, orderEvent.ClientOrderId)
            && MatchesGuid(query.RunSessionId, orderEvent.RunSessionId, orderEvent.ContextJson, "runSessionId", "RunSessionId", "sessionId", "SessionId")
            && MatchesGuid(query.RiskEventId, null, orderEvent.ContextJson, "riskEventId", "RiskEventId")
            && MatchesText(query.CorrelationId, orderEvent.CorrelationId, orderEvent.ContextJson, "correlationId", "CorrelationId");

    private static bool MatchesOrder(
        OrderDto order,
        ReplayExportQuery query,
        DateTimeOffset? effectiveFrom,
        DateTimeOffset? effectiveTo)
        => MatchesTime(order.CreatedAtUtc, effectiveFrom, effectiveTo)
            && MatchesText(query.StrategyId, order.StrategyId)
            && MatchesText(query.MarketId, order.MarketId)
            && MatchesGuid(query.OrderId, order.Id)
            && MatchesText(query.ClientOrderId, order.ClientOrderId)
            && MatchesText(query.CorrelationId, order.CorrelationId);

    private static bool MatchesTrade(
        TradeDto trade,
        ReplayExportQuery query,
        DateTimeOffset? effectiveFrom,
        DateTimeOffset? effectiveTo)
        => MatchesTime(trade.CreatedAtUtc, effectiveFrom, effectiveTo)
            && MatchesText(query.StrategyId, trade.StrategyId)
            && MatchesText(query.MarketId, trade.MarketId)
            && MatchesGuid(query.OrderId, trade.OrderId)
            && MatchesText(query.ClientOrderId, trade.ClientOrderId)
            && MatchesText(query.CorrelationId, trade.CorrelationId);

    private static bool MatchesRiskEvent(
        RiskEventRecord riskEvent,
        ReplayExportQuery query,
        DateTimeOffset? effectiveFrom,
        DateTimeOffset? effectiveTo)
        => MatchesTime(riskEvent.CreatedAtUtc, effectiveFrom, effectiveTo)
            && MatchesText(query.StrategyId, riskEvent.StrategyId, riskEvent.ContextJson, "strategyId", "StrategyId")
            && MatchesText(query.MarketId, riskEvent.MarketId, riskEvent.ContextJson, "marketId", "MarketId")
            && MatchesGuid(query.OrderId, null, riskEvent.ContextJson, "orderId", "OrderId")
            && MatchesText(query.ClientOrderId, null, riskEvent.ContextJson, "clientOrderId", "ClientOrderId")
            && MatchesGuid(query.RunSessionId, null, riskEvent.ContextJson, "runSessionId", "RunSessionId", "sessionId", "SessionId")
            && MatchesGuid(query.RiskEventId, riskEvent.Id, riskEvent.ContextJson, "riskEventId", "RiskEventId")
            && MatchesText(query.CorrelationId, null, riskEvent.ContextJson, "correlationId", "CorrelationId");

    private static bool MatchesTime(DateTimeOffset timestampUtc, DateTimeOffset? fromUtc, DateTimeOffset? toUtc)
    {
        if (fromUtc.HasValue && timestampUtc < fromUtc.Value)
        {
            return false;
        }

        return !toUtc.HasValue || timestampUtc <= toUtc.Value;
    }

    private static bool MatchesText(string? expected, string? actual, string? detailJson = null, params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }

        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
            || JsonContainsString(detailJson, expected, propertyNames);
    }

    private static bool MatchesGuid(Guid? expected, Guid? actual, string? detailJson = null, params string[] propertyNames)
    {
        if (!expected.HasValue || expected == Guid.Empty)
        {
            return true;
        }

        return actual == expected.Value || JsonContainsString(detailJson, expected.Value.ToString(), propertyNames);
    }

    private static bool JsonContainsString(string? json, string expected, params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(json) || propertyNames.Length == 0)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonContainsString(document.RootElement, expected, propertyNames);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool JsonContainsString(JsonElement element, string expected, params string[] propertyNames)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (propertyNames.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    && string.Equals(JsonScalarToString(property.Value), expected, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (JsonContainsString(property.Value, expected, propertyNames))
                {
                    return true;
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (JsonContainsString(item, expected, propertyNames))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static ReplayRunSessionRecord ToReplayRunSession(PaperRunSessionRecord session)
        => new(
            session.SessionId,
            session.ExecutionMode,
            session.ConfigVersion,
            session.Strategies,
            SanitizeJson(session.RiskProfileJson) ?? "{}",
            session.OperatorSource,
            session.StartedAtUtc,
            session.StoppedAtUtc,
            session.StopReason,
            session.IsActive,
            session.Recovered);

    private static ReplayDecisionRecord ToReplayDecision(StrategyDecisionRecord decision)
        => new(
            decision.DecisionId,
            decision.StrategyId,
            decision.Action,
            decision.Reason,
            decision.MarketId,
            SanitizeJson(decision.ContextJson),
            decision.TimestampUtc,
            decision.ConfigVersion,
            decision.CorrelationId,
            decision.ExecutionMode,
            decision.RunSessionId);

    private static ReplayOrderEventRecord ToReplayOrderEvent(OrderEventDto orderEvent)
        => new(
            orderEvent.Id,
            orderEvent.OrderId,
            orderEvent.ClientOrderId,
            orderEvent.StrategyId,
            orderEvent.MarketId,
            orderEvent.EventType,
            orderEvent.Status,
            orderEvent.Message,
            SanitizeJson(orderEvent.ContextJson),
            orderEvent.CorrelationId,
            orderEvent.CreatedAtUtc,
            orderEvent.RunSessionId);

    private static ReplayOrderRecord ToReplayOrder(OrderDto order)
        => new(
            order.Id,
            order.MarketId,
            order.TokenId,
            order.StrategyId,
            order.ClientOrderId,
            order.ExchangeOrderId,
            order.CorrelationId,
            order.Outcome,
            order.Side,
            order.OrderType,
            order.TimeInForce,
            order.GoodTilDateUtc,
            order.NegRisk,
            order.Price,
            order.Quantity,
            order.FilledQuantity,
            order.Status,
            order.RejectionReason,
            order.CreatedAtUtc,
            order.UpdatedAtUtc);

    private static ReplayTradeRecord ToReplayTrade(TradeDto trade)
        => new(
            trade.Id,
            trade.OrderId,
            trade.ClientOrderId,
            trade.StrategyId,
            trade.MarketId,
            trade.TokenId,
            trade.Outcome,
            trade.Side,
            trade.Price,
            trade.Quantity,
            trade.ExchangeTradeId,
            trade.Fee,
            trade.Notional,
            trade.CorrelationId,
            trade.CreatedAtUtc);

    private static ReplayPositionRecord ToReplayPosition(PositionDto position)
        => new(
            position.Id,
            position.MarketId,
            position.Outcome,
            position.Quantity,
            position.AverageCost,
            position.RealizedPnl,
            position.Notional,
            position.UpdatedAtUtc);

    private static ReplayRiskEventRecord ToReplayRiskEvent(RiskEventRecord riskEvent)
        => new(
            riskEvent.Id,
            riskEvent.Code,
            riskEvent.Severity,
            riskEvent.Message,
            riskEvent.StrategyId,
            SanitizeJson(riskEvent.ContextJson),
            riskEvent.CreatedAtUtc,
            riskEvent.MarketId);

    private static ReplayReadinessReport ToReplayReadinessReport(ReadinessReport report)
        => new(
            report.ContractVersion,
            report.CheckedAtUtc,
            report.Status,
            report.Checks.Select(check => new ReplayReadinessCheck(
                check.Id,
                check.Category,
                check.Requirement,
                check.Status,
                check.Source,
                check.LastCheckedAtUtc,
                check.Summary,
                check.RemediationHint,
                check.Evidence.ToDictionary(
                    pair => pair.Key,
                    pair => IsSensitivePropertyName(pair.Key) ? "[redacted]" : SanitizeText(pair.Value),
                    StringComparer.Ordinal))).ToArray(),
            report.Capabilities.Select(capability => new ReplayReadinessCapability(
                capability.Capability,
                capability.Status,
                capability.BlockingCheckIds,
                capability.Summary)).ToArray());

    private static IReadOnlyList<ReplayStrategyConfigVersion> BuildStrategyConfigVersions(
        PaperRunSessionRecord? runSession,
        IReadOnlyList<StrategyDecisionRecord> decisions)
    {
        var versions = new List<ReplayStrategyConfigVersion>();
        if (runSession is not null)
        {
            versions.AddRange(runSession.Strategies.Select(strategyId => new ReplayStrategyConfigVersion(
                strategyId,
                runSession.ConfigVersion,
                "run_session",
                runSession.StartedAtUtc)));
        }

        versions.AddRange(decisions.Select(decision => new ReplayStrategyConfigVersion(
            decision.StrategyId,
            decision.ConfigVersion,
            "strategy_decision",
            decision.TimestampUtc)));

        return versions
            .GroupBy(version => new { version.StrategyId, version.ConfigVersion, version.Source })
            .Select(group => group.OrderBy(item => item.ObservedAtUtc).First())
            .OrderBy(version => version.StrategyId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(version => version.ConfigVersion, StringComparer.OrdinalIgnoreCase)
            .ThenBy(version => version.Source, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> BuildCompletenessNotes(
        ReplayExportQuery query,
        PaperRunSessionRecord? runSession,
        string? readinessNote,
        IReadOnlyList<StrategyDecisionRecord> decisions,
        IReadOnlyList<OrderEventDto> orderEvents,
        IReadOnlyList<OrderDto> orders,
        IReadOnlyList<TradeDto> trades,
        IReadOnlyList<PositionDto> positions,
        IReadOnlyList<RiskEventRecord> riskEvents)
    {
        var notes = new List<string>();
        if (query.RunSessionId.HasValue && runSession is null)
        {
            notes.Add($"Run session was not found: {query.RunSessionId}");
        }

        if (readinessNote is not null)
        {
            notes.Add(readinessNote);
        }

        if (decisions.Count == 0)
        {
            notes.Add("No strategy decisions matched the replay query.");
        }

        if (orderEvents.Count == 0)
        {
            notes.Add("No order events matched the replay query.");
        }

        if (orders.Count == 0)
        {
            notes.Add("No orders matched the replay query.");
        }

        if (trades.Count == 0)
        {
            notes.Add("No trades matched the replay query.");
        }

        if (positions.Count == 0)
        {
            notes.Add("No non-zero positions matched the replay query markets.");
        }

        if (riskEvents.Count == 0)
        {
            notes.Add("No risk events matched the replay query.");
        }

        return notes;
    }

    private static ReplayExportReferences BuildExportReferences(ReplayExportQuery query)
    {
        var queryString = BuildQueryString(query);
        return new ReplayExportReferences(
            $"/api/replay-exports{queryString}",
            BuildCliReference(query),
            "docs/operations/replay-export-schema.md");
    }

    private static string BuildQueryString(ReplayExportQuery query)
    {
        var parts = new List<string>();
        AddQueryPart(parts, "strategyId", query.StrategyId);
        AddQueryPart(parts, "marketId", query.MarketId);
        AddQueryPart(parts, "orderId", query.OrderId?.ToString());
        AddQueryPart(parts, "clientOrderId", query.ClientOrderId);
        AddQueryPart(parts, "runSessionId", query.RunSessionId?.ToString());
        AddQueryPart(parts, "riskEventId", query.RiskEventId?.ToString());
        AddQueryPart(parts, "correlationId", query.CorrelationId);
        AddQueryPart(parts, "fromUtc", query.FromUtc?.ToString("O"));
        AddQueryPart(parts, "toUtc", query.ToUtc?.ToString("O"));
        AddQueryPart(parts, "limit", query.Limit.ToString());
        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }

    private static void AddQueryPart(List<string> parts, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}");
        }
    }

    private static string BuildCliReference(ReplayExportQuery query)
    {
        var builder = new StringBuilder("autotrade export replay-package --json");
        AppendCliOption(builder, "--strategy-id", query.StrategyId);
        AppendCliOption(builder, "--market-id", query.MarketId);
        AppendCliOption(builder, "--order-id", query.OrderId?.ToString());
        AppendCliOption(builder, "--client-order-id", query.ClientOrderId);
        AppendCliOption(builder, "--session-id", query.RunSessionId?.ToString());
        AppendCliOption(builder, "--risk-event-id", query.RiskEventId?.ToString());
        AppendCliOption(builder, "--correlation-id", query.CorrelationId);
        AppendCliOption(builder, "--from", query.FromUtc?.ToString("O"));
        AppendCliOption(builder, "--to", query.ToUtc?.ToString("O"));
        builder.Append(" --limit ").Append(query.Limit);
        return builder.ToString();
    }

    private static void AppendCliOption(StringBuilder builder, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.Append(' ').Append(name).Append(' ').Append('"').Append(value.Replace("\"", "\\\"")).Append('"');
        }
    }

    private static string? SanitizeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                WriteSanitizedJson(writer, document.RootElement, null);
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return SanitizeText(json);
        }
    }

    private static void WriteSanitizedJson(Utf8JsonWriter writer, JsonElement element, string? propertyName)
    {
        if (propertyName is not null && IsSensitivePropertyName(propertyName))
        {
            writer.WriteStringValue("[redacted]");
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    WriteSanitizedJson(writer, property.Value, property.Name);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteSanitizedJson(writer, item, null);
                }
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool IsSensitivePropertyName(string propertyName)
    {
        var normalized = propertyName.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return normalized.Contains("password", StringComparison.Ordinal)
            || normalized.Contains("secret", StringComparison.Ordinal)
            || normalized.Contains("privatekey", StringComparison.Ordinal)
            || normalized.Contains("apikey", StringComparison.Ordinal)
            || normalized.Contains("authorization", StringComparison.Ordinal)
            || normalized.Contains("credential", StringComparison.Ordinal)
            || normalized.Contains("signature", StringComparison.Ordinal)
            || normalized.Contains("passphrase", StringComparison.Ordinal)
            || normalized.Contains("mnemonic", StringComparison.Ordinal);
    }

    private static string SanitizeText(string value)
        => RawSecretRegex.Replace(value, "$1[redacted]");

    private static string? JsonScalarToString(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => null
        };
}
