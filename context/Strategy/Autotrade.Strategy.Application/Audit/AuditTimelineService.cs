using System.Text.Json;
using Autotrade.Strategy.Application.Decisions;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.Contract.Risk;

namespace Autotrade.Strategy.Application.Audit;

public interface IAuditTimelineService
{
    Task<AuditTimeline> QueryAsync(
        AuditTimelineQuery query,
        CancellationToken cancellationToken = default);
}

public sealed class AuditTimelineService(
    IStrategyDecisionQueryService decisionQueryService,
    IOrderEventRepository orderEventRepository,
    IRiskEventRepository riskEventRepository,
    ICommandAuditRepository commandAuditRepository) : IAuditTimelineService
{
    private const int MaxLimit = 1000;
    private const int MaxSourceFetchLimit = 5000;

    private readonly IStrategyDecisionQueryService _decisionQueryService =
        decisionQueryService ?? throw new ArgumentNullException(nameof(decisionQueryService));
    private readonly IOrderEventRepository _orderEventRepository =
        orderEventRepository ?? throw new ArgumentNullException(nameof(orderEventRepository));
    private readonly IRiskEventRepository _riskEventRepository =
        riskEventRepository ?? throw new ArgumentNullException(nameof(riskEventRepository));
    private readonly ICommandAuditRepository _commandAuditRepository =
        commandAuditRepository ?? throw new ArgumentNullException(nameof(commandAuditRepository));

    public async Task<AuditTimeline> QueryAsync(
        AuditTimelineQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var normalizedLimit = NormalizeLimit(query.Limit);
        var sourceLimit = SourceFetchLimit(normalizedLimit);
        var items = new List<AuditTimelineItem>();

        items.AddRange(await LoadDecisionItemsAsync(query, sourceLimit, cancellationToken).ConfigureAwait(false));
        items.AddRange(await LoadOrderEventItemsAsync(query, sourceLimit, cancellationToken).ConfigureAwait(false));
        items.AddRange(await LoadRiskEventItemsAsync(query, sourceLimit, cancellationToken).ConfigureAwait(false));
        items.AddRange(await LoadCommandAuditItemsAsync(query, sourceLimit, cancellationToken).ConfigureAwait(false));

        var result = items
            .Where(item => MatchesQuery(item, query))
            .GroupBy(item => new { item.Type, item.ItemId })
            .Select(group => group.First())
            .OrderByDescending(item => item.TimestampUtc)
            .ThenBy(item => item.Type)
            .ThenBy(item => item.ItemId)
            .Take(normalizedLimit)
            .ToArray();

        return new AuditTimeline(
            DateTimeOffset.UtcNow,
            result.Length,
            normalizedLimit,
            query with { Limit = normalizedLimit },
            result);
    }

    private async Task<IReadOnlyList<AuditTimelineItem>> LoadDecisionItemsAsync(
        AuditTimelineQuery query,
        int sourceLimit,
        CancellationToken cancellationToken)
    {
        var decisions = await _decisionQueryService
            .QueryRecordsAsync(
                new StrategyDecisionQuery(
                    query.StrategyId,
                    query.MarketId,
                    query.FromUtc,
                    query.ToUtc,
                    sourceLimit,
                    CorrelationId: query.CorrelationId,
                    RunSessionId: query.RunSessionId),
                cancellationToken)
            .ConfigureAwait(false);

        return decisions.Select(ToDecisionItem).ToArray();
    }

    private async Task<IReadOnlyList<AuditTimelineItem>> LoadOrderEventItemsAsync(
        AuditTimelineQuery query,
        int sourceLimit,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<OrderEventDto> events;
        if (query.OrderId.HasValue)
        {
            events = await _orderEventRepository
                .GetByOrderIdAsync(query.OrderId.Value, cancellationToken)
                .ConfigureAwait(false);
        }
        else if (!string.IsNullOrWhiteSpace(query.ClientOrderId))
        {
            events = await _orderEventRepository
                .GetByClientOrderIdAsync(query.ClientOrderId, cancellationToken)
                .ConfigureAwait(false);
        }
        else if (query.RunSessionId.HasValue)
        {
            events = await _orderEventRepository
                .GetByRunSessionIdAsync(query.RunSessionId.Value, sourceLimit, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var page = await _orderEventRepository
                .GetPagedAsync(
                    1,
                    sourceLimit,
                    query.StrategyId,
                    query.MarketId,
                    null,
                    query.FromUtc,
                    query.ToUtc,
                    cancellationToken)
                .ConfigureAwait(false);
            events = page.Items;
        }

        return events.Select(ToOrderEventItem).ToArray();
    }

    private async Task<IReadOnlyList<AuditTimelineItem>> LoadRiskEventItemsAsync(
        AuditTimelineQuery query,
        int sourceLimit,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RiskEventRecord> riskEvents;
        if (query.RiskEventId.HasValue)
        {
            var riskEvent = await _riskEventRepository
                .GetAsync(query.RiskEventId.Value, cancellationToken)
                .ConfigureAwait(false);
            riskEvents = riskEvent is null ? Array.Empty<RiskEventRecord>() : new[] { riskEvent };
        }
        else
        {
            var canUseRepositoryStrategyFilter = string.IsNullOrWhiteSpace(query.MarketId)
                && !query.OrderId.HasValue
                && string.IsNullOrWhiteSpace(query.ClientOrderId)
                && !query.RunSessionId.HasValue
                && string.IsNullOrWhiteSpace(query.CorrelationId);

            riskEvents = await _riskEventRepository
                .QueryAsync(
                    canUseRepositoryStrategyFilter ? query.StrategyId : null,
                    query.FromUtc,
                    query.ToUtc,
                    sourceLimit,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return riskEvents.Select(ToRiskEventItem).ToArray();
    }

    private async Task<IReadOnlyList<AuditTimelineItem>> LoadCommandAuditItemsAsync(
        AuditTimelineQuery query,
        int sourceLimit,
        CancellationToken cancellationToken)
    {
        var commands = await _commandAuditRepository
            .QueryAsync(new CommandAuditQuery(query.FromUtc, query.ToUtc, sourceLimit), cancellationToken)
            .ConfigureAwait(false);

        return commands.Select(command => ToCommandAuditItem(command)).ToArray();
    }

    private static AuditTimelineItem ToDecisionItem(StrategyDecisionRecord decision)
        => new(
            decision.DecisionId,
            decision.TimestampUtc,
            AuditTimelineItemType.StrategyDecision,
            "Strategy",
            decision.StrategyId,
            $"{decision.Action}: {decision.Reason}",
            $"strategy-decisions/{decision.DecisionId}",
            decision.StrategyId,
            decision.MarketId,
            TryFindGuid(decision.ContextJson, "orderId", "OrderId"),
            TryFindString(decision.ContextJson, "clientOrderId", "ClientOrderId"),
            decision.RunSessionId ?? TryFindGuid(decision.ContextJson, "runSessionId", "RunSessionId"),
            TryFindGuid(decision.ContextJson, "riskEventId", "RiskEventId"),
            decision.CorrelationId ?? TryFindString(decision.ContextJson, "correlationId", "CorrelationId"),
            decision.ContextJson);

    private static AuditTimelineItem ToOrderEventItem(OrderEventDto orderEvent)
        => new(
            orderEvent.Id,
            orderEvent.CreatedAtUtc,
            AuditTimelineItemType.OrderEvent,
            "Trading",
            orderEvent.StrategyId,
            $"{orderEvent.EventType}: {orderEvent.Message}",
            $"order-events/{orderEvent.Id}",
            orderEvent.StrategyId,
            orderEvent.MarketId,
            orderEvent.OrderId,
            orderEvent.ClientOrderId,
            orderEvent.RunSessionId,
            TryFindGuid(orderEvent.ContextJson, "riskEventId", "RiskEventId"),
            orderEvent.CorrelationId ?? TryFindString(orderEvent.ContextJson, "correlationId", "CorrelationId"),
            orderEvent.ContextJson);

    private static AuditTimelineItem ToRiskEventItem(RiskEventRecord riskEvent)
        => new(
            riskEvent.Id,
            riskEvent.CreatedAtUtc,
            AuditTimelineItemType.RiskEvent,
            "Risk",
            riskEvent.StrategyId ?? "RiskManager",
            $"{riskEvent.Severity}: {riskEvent.Code} - {riskEvent.Message}",
            $"risk-events/{riskEvent.Id}",
            riskEvent.StrategyId ?? TryFindString(riskEvent.ContextJson, "strategyId", "StrategyId"),
            riskEvent.MarketId ?? TryFindString(riskEvent.ContextJson, "marketId", "MarketId"),
            TryFindGuid(riskEvent.ContextJson, "orderId", "OrderId"),
            TryFindString(riskEvent.ContextJson, "clientOrderId", "ClientOrderId"),
            TryFindGuid(riskEvent.ContextJson, "runSessionId", "RunSessionId"),
            riskEvent.Id,
            TryFindString(riskEvent.ContextJson, "correlationId", "CorrelationId"),
            riskEvent.ContextJson);

    private static AuditTimelineItem ToCommandAuditItem(Autotrade.Strategy.Domain.Entities.CommandAuditLog command)
        => new(
            command.Id,
            command.CreatedAtUtc,
            AuditTimelineItemType.CommandAudit,
            "CommandAudit",
            command.Actor ?? "operator",
            $"{command.CommandName}: {(command.Success ? "Succeeded" : "Failed")}",
            $"command-audit/{command.Id}",
            TryFindString(command.ArgumentsJson, "strategyId", "StrategyId"),
            TryFindString(command.ArgumentsJson, "marketId", "MarketId"),
            TryFindGuid(command.ArgumentsJson, "orderId", "OrderId"),
            TryFindString(command.ArgumentsJson, "clientOrderId", "ClientOrderId"),
            TryFindGuid(command.ArgumentsJson, "runSessionId", "RunSessionId", "sessionId", "SessionId"),
            TryFindGuid(command.ArgumentsJson, "riskEventId", "RiskEventId"),
            TryFindString(command.ArgumentsJson, "correlationId", "CorrelationId"),
            command.ArgumentsJson);

    private static bool MatchesQuery(AuditTimelineItem item, AuditTimelineQuery query)
    {
        if (query.FromUtc.HasValue && item.TimestampUtc < query.FromUtc.Value)
        {
            return false;
        }

        if (query.ToUtc.HasValue && item.TimestampUtc > query.ToUtc.Value)
        {
            return false;
        }

        return MatchesText(query.StrategyId, item.StrategyId, item.DetailJson, "strategyId", "StrategyId")
            && MatchesText(query.MarketId, item.MarketId, item.DetailJson, "marketId", "MarketId")
            && MatchesGuid(query.OrderId, item.OrderId, item.DetailJson, "orderId", "OrderId")
            && MatchesText(query.ClientOrderId, item.ClientOrderId, item.DetailJson, "clientOrderId", "ClientOrderId")
            && MatchesGuid(query.RunSessionId, item.RunSessionId, item.DetailJson, "runSessionId", "RunSessionId", "sessionId", "SessionId")
            && MatchesGuid(query.RiskEventId, item.RiskEventId, item.DetailJson, "riskEventId", "RiskEventId")
            && MatchesText(query.CorrelationId, item.CorrelationId, item.DetailJson, "correlationId", "CorrelationId");
    }

    private static bool MatchesText(string? expected, string? actual, string? detailJson, params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }

        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
            || JsonContainsString(detailJson, expected, propertyNames);
    }

    private static bool MatchesGuid(Guid? expected, Guid? actual, string? detailJson, params string[] propertyNames)
    {
        if (!expected.HasValue)
        {
            return true;
        }

        return actual == expected.Value || JsonContainsGuid(detailJson, expected.Value, propertyNames);
    }

    private static int NormalizeLimit(int limit) => Math.Clamp(limit, 1, MaxLimit);

    private static int SourceFetchLimit(int normalizedLimit)
        => Math.Clamp(normalizedLimit * 4, normalizedLimit, MaxSourceFetchLimit);

    private static Guid? TryFindGuid(string? json, params string[] propertyNames)
        => Guid.TryParse(TryFindString(json, propertyNames), out var value) ? value : null;

    private static string? TryFindString(string? json, params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return TryFindString(document.RootElement, propertyNames);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryFindString(JsonElement element, params string[] propertyNames)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (propertyNames.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    var value = JsonScalarToString(property.Value);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }

                var nested = TryFindString(property.Value, propertyNames);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = TryFindString(item, propertyNames);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static bool JsonContainsString(string? json, string expected, params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(json))
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
                if (propertyNames.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    var value = JsonScalarToString(property.Value);
                    if (string.Equals(value, expected, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
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

    private static bool JsonContainsGuid(string? json, Guid expected, params string[] propertyNames)
        => JsonContainsString(json, expected.ToString(), propertyNames);

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
