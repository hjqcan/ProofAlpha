using System.Globalization;
using System.Text.Json;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Trading.Application.Risk;

public sealed class RiskDrilldownService(
    IRiskEventRepository riskEventRepository,
    IOrderEventRepository orderEventRepository,
    IOrderRepository orderRepository,
    IRiskManager riskManager) : IRiskDrilldownService
{
    private const int MaxLimit = 1000;

    private readonly IRiskEventRepository _riskEventRepository =
        riskEventRepository ?? throw new ArgumentNullException(nameof(riskEventRepository));
    private readonly IOrderEventRepository _orderEventRepository =
        orderEventRepository ?? throw new ArgumentNullException(nameof(orderEventRepository));
    private readonly IOrderRepository _orderRepository =
        orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
    private readonly IRiskManager _riskManager =
        riskManager ?? throw new ArgumentNullException(nameof(riskManager));

    public async Task<RiskEventDrilldown?> GetRiskEventAsync(
        Guid riskEventId,
        CancellationToken cancellationToken = default)
    {
        var riskEvent = await _riskEventRepository.GetAsync(riskEventId, cancellationToken).ConfigureAwait(false);
        if (riskEvent is null)
        {
            return null;
        }

        var affectedOrders = await BuildAffectedOrdersAsync(riskEvent, cancellationToken).ConfigureAwait(false);
        var exposure = TryBuildExposureFromRiskEvent(riskEvent);
        var killSwitch = BuildKillSwitchLink(riskEvent);

        return new RiskEventDrilldown(
            DateTimeOffset.UtcNow,
            riskEvent,
            BuildTrigger(riskEvent),
            BuildAction(riskEvent),
            affectedOrders,
            exposure,
            killSwitch,
            new RiskDrilldownSourceReferences(
                $"/api/control-room/risk/events/{riskEvent.Id}",
                $"/api/control-room/risk/events/{riskEvent.Id}/csv",
                [riskEvent.Id],
                affectedOrders
                    .Select(order => TryGuidFromReference(order.DetailReference))
                    .Where(id => id.HasValue)
                    .Select(id => id!.Value)
                    .ToArray()));
    }

    public async Task<UnhedgedExposureDrilldownResponse> QueryUnhedgedExposuresAsync(
        RiskDrilldownQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var normalizedLimit = Math.Clamp(query.Limit, 1, MaxLimit);
        var exposures = new List<UnhedgedExposureDrilldown>();

        if (!query.RiskEventId.HasValue)
        {
            exposures.AddRange(_riskManager.GetStateSnapshot()
                .UnhedgedExposures
                .Select(ToCurrentExposure));
        }

        var riskEvents = await LoadExposureRiskEventsAsync(query, normalizedLimit, cancellationToken)
            .ConfigureAwait(false);
        exposures.AddRange(riskEvents.SelectNotNull(TryBuildExposureFromRiskEvent));

        var result = exposures
            .Where(exposure => Matches(query.StrategyId, exposure.StrategyId)
                && Matches(query.MarketId, exposure.MarketId)
                && (!query.FromUtc.HasValue || exposure.StartedAtUtc >= query.FromUtc.Value || exposure.EndedAtUtc >= query.FromUtc.Value)
                && (!query.ToUtc.HasValue || exposure.StartedAtUtc <= query.ToUtc.Value))
            .GroupBy(exposure => exposure.EvidenceId?.ToString()
                ?? $"{exposure.StrategyId}:{exposure.MarketId}:{exposure.TokenId}:{exposure.StartedAtUtc:O}")
            .Select(group => group.First())
            .OrderByDescending(exposure => exposure.StartedAtUtc)
            .Take(normalizedLimit)
            .ToArray();

        return new UnhedgedExposureDrilldownResponse(
            DateTimeOffset.UtcNow,
            result.Length,
            normalizedLimit,
            query with { Limit = normalizedLimit },
            result);
    }

    private async Task<IReadOnlyList<RiskEventRecord>> LoadExposureRiskEventsAsync(
        RiskDrilldownQuery query,
        int limit,
        CancellationToken cancellationToken)
    {
        if (query.RiskEventId.HasValue)
        {
            var riskEvent = await _riskEventRepository
                .GetAsync(query.RiskEventId.Value, cancellationToken)
                .ConfigureAwait(false);
            return riskEvent is null ? Array.Empty<RiskEventRecord>() : [riskEvent];
        }

        var events = await _riskEventRepository
            .QueryAsync(query.StrategyId, query.FromUtc, query.ToUtc, limit, cancellationToken)
            .ConfigureAwait(false);

        return events.Where(IsUnhedgedExposureEvent).ToArray();
    }

    private async Task<IReadOnlyList<RiskAffectedOrder>> BuildAffectedOrdersAsync(
        RiskEventRecord riskEvent,
        CancellationToken cancellationToken)
    {
        var affected = new List<RiskAffectedOrder>();
        var orderIds = FindGuids(riskEvent.ContextJson, "orderId", "OrderId", "orderIds", "OrderIds")
            .ToHashSet();
        var clientOrderIds = FindStrings(riskEvent.ContextJson, "clientOrderId", "ClientOrderId", "clientOrderIds", "ClientOrderIds")
            .Where(value => !Guid.TryParse(value, out _))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var orderId in orderIds)
        {
            var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
            if (order is not null)
            {
                affected.Add(new RiskAffectedOrder(
                    order.Id,
                    order.ClientOrderId,
                    order.StrategyId,
                    order.MarketId,
                    order.Status.ToString(),
                    "orders",
                    $"orders/{order.Id}"));
            }

            var events = await _orderEventRepository.GetByOrderIdAsync(orderId, cancellationToken).ConfigureAwait(false);
            affected.AddRange(events.Select(ToAffectedOrder));
        }

        foreach (var clientOrderId in clientOrderIds)
        {
            var order = await _orderRepository.GetByClientOrderIdAsync(clientOrderId, cancellationToken).ConfigureAwait(false);
            if (order is not null)
            {
                affected.Add(new RiskAffectedOrder(
                    order.Id,
                    order.ClientOrderId,
                    order.StrategyId,
                    order.MarketId,
                    order.Status.ToString(),
                    "orders",
                    $"orders/{order.Id}"));
            }

            var events = await _orderEventRepository.GetByClientOrderIdAsync(clientOrderId, cancellationToken).ConfigureAwait(false);
            affected.AddRange(events.Select(ToAffectedOrder));
        }

        if (affected.Count == 0
            && !string.IsNullOrWhiteSpace(riskEvent.StrategyId)
            && !string.IsNullOrWhiteSpace(riskEvent.MarketId))
        {
            var page = await _orderEventRepository
                .GetPagedAsync(
                    1,
                    50,
                    riskEvent.StrategyId,
                    riskEvent.MarketId,
                    null,
                    riskEvent.CreatedAtUtc.AddMinutes(-10),
                    riskEvent.CreatedAtUtc.AddMinutes(10),
                    cancellationToken)
                .ConfigureAwait(false);
            affected.AddRange(page.Items.Select(ToAffectedOrder));
        }

        return affected
            .GroupBy(order => $"{order.Source}:{order.DetailReference}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(order => order.ClientOrderId ?? order.OrderId?.ToString())
            .ToArray();
    }

    private static RiskAffectedOrder ToAffectedOrder(OrderEventDto item)
        => new(
            item.OrderId,
            item.ClientOrderId,
            item.StrategyId,
            item.MarketId,
            item.Status.ToString(),
            "order-events",
            $"order-events/{item.Id}");

    private static RiskTriggerDrilldown BuildTrigger(RiskEventRecord riskEvent)
    {
        var current = TryFindDecimal(riskEvent.ContextJson, "currentValue", "CurrentValue", "current", "Current", "value", "Value", "notional", "Notional");
        var threshold = TryFindDecimal(riskEvent.ContextJson, "threshold", "Threshold", "limit", "Limit", "max", "Max", "configuredTimeoutSeconds", "ConfiguredTimeoutSeconds");
        var state = TryFindString(riskEvent.ContextJson, "state", "State")
            ?? (riskEvent.Severity is RiskSeverity.Critical or RiskSeverity.Warning ? "breached" : "observed");

        return new RiskTriggerDrilldown(
            riskEvent.Message,
            TryFindString(riskEvent.ContextJson, "limitName", "LimitName", "metric", "Metric") ?? DeriveLimitName(riskEvent.Code),
            current,
            threshold,
            TryFindString(riskEvent.ContextJson, "unit", "Unit"),
            state);
    }

    private static RiskActionDrilldown BuildAction(RiskEventRecord riskEvent)
    {
        var selected = TryFindString(riskEvent.ContextJson, "selectedAction", "SelectedAction", "action", "Action", "exitAction", "ExitAction")
            ?? DeriveSelectedAction(riskEvent.Code);

        return new RiskActionDrilldown(
            selected,
            TryFindString(riskEvent.ContextJson, "mitigationResult", "MitigationResult", "result", "Result") ?? selected,
            riskEvent.Code);
    }

    private RiskKillSwitchLink? BuildKillSwitchLink(RiskEventRecord riskEvent)
    {
        if (!string.IsNullOrWhiteSpace(riskEvent.StrategyId))
        {
            var strategyState = _riskManager.GetStrategyKillSwitchState(riskEvent.StrategyId);
            if (!IsLinkedKillSwitch(strategyState, riskEvent))
            {
                return null;
            }

            return new RiskKillSwitchLink(
                "Strategy",
                strategyState.Level.ToString(),
                strategyState.ReasonCode ?? riskEvent.Code,
                strategyState.Reason ?? riskEvent.Message,
                strategyState.ActivatedAtUtc,
                riskEvent.Id);
        }

        var state = _riskManager.GetKillSwitchState();
        if (!IsLinkedKillSwitch(state, riskEvent))
        {
            return null;
        }

        return new RiskKillSwitchLink(
            "Global",
            state.Level.ToString(),
            state.ReasonCode ?? riskEvent.Code,
            state.Reason ?? riskEvent.Message,
            state.ActivatedAtUtc,
            riskEvent.Id);
    }

    private static bool IsLinkedKillSwitch(KillSwitchState state, RiskEventRecord riskEvent)
        => state.IsActive
            && (string.Equals(state.ReasonCode, riskEvent.Code, StringComparison.OrdinalIgnoreCase)
                || ContainsIgnoreCase(riskEvent.Code, "KILL_SWITCH")
                || ContainsIgnoreCase(riskEvent.Code, "KILL")
                || ContainsIgnoreCase(riskEvent.Message, "kill switch"));

    private static UnhedgedExposureDrilldown ToCurrentExposure(UnhedgedExposureSnapshot snapshot)
    {
        var duration = Math.Max(0d, (DateTimeOffset.UtcNow - snapshot.StartedAtUtc).TotalSeconds);
        return new UnhedgedExposureDrilldown(
            EvidenceId: null,
            snapshot.StrategyId,
            snapshot.MarketId,
            snapshot.TokenId,
            snapshot.HedgeTokenId,
            snapshot.Outcome.ToString(),
            snapshot.Side.ToString(),
            snapshot.Quantity,
            snapshot.Price,
            snapshot.Notional,
            duration,
            snapshot.StartedAtUtc,
            EndedAtUtc: null,
            TimeoutSeconds: null,
            "Open",
            "Pending",
            "risk_state");
    }

    private static UnhedgedExposureDrilldown? TryBuildExposureFromRiskEvent(RiskEventRecord riskEvent)
    {
        if (!IsUnhedgedExposureEvent(riskEvent))
        {
            return null;
        }

        var startedAt = TryFindDateTimeOffset(riskEvent.ContextJson, "startedAtUtc", "StartedAtUtc") ?? riskEvent.CreatedAtUtc;
        var endedAt = TryFindDateTimeOffset(riskEvent.ContextJson, "expiredAtUtc", "ExpiredAtUtc", "endedAtUtc", "EndedAtUtc")
            ?? riskEvent.CreatedAtUtc;
        var duration = TryFindDouble(riskEvent.ContextJson, "exposureDurationSeconds", "ExposureDurationSeconds")
            ?? Math.Max(0d, (endedAt - startedAt).TotalSeconds);
        var action = TryFindString(riskEvent.ContextJson, "exitAction", "ExitAction", "selectedAction", "SelectedAction")
            ?? DeriveSelectedAction(riskEvent.Code);

        return new UnhedgedExposureDrilldown(
            riskEvent.Id,
            riskEvent.StrategyId ?? TryFindString(riskEvent.ContextJson, "strategyId", "StrategyId") ?? "unknown",
            riskEvent.MarketId ?? TryFindString(riskEvent.ContextJson, "marketId", "MarketId") ?? "unknown",
            TryFindString(riskEvent.ContextJson, "tokenId", "TokenId") ?? string.Empty,
            TryFindString(riskEvent.ContextJson, "hedgeTokenId", "HedgeTokenId") ?? string.Empty,
            TryFindString(riskEvent.ContextJson, "outcome", "Outcome") ?? "Unknown",
            TryFindString(riskEvent.ContextJson, "side", "Side") ?? "Unknown",
            TryFindDecimal(riskEvent.ContextJson, "quantity", "Quantity") ?? 0m,
            TryFindDecimal(riskEvent.ContextJson, "price", "Price") ?? 0m,
            TryFindDecimal(riskEvent.ContextJson, "notional", "Notional") ?? 0m,
            duration,
            startedAt,
            endedAt,
            TryFindDouble(riskEvent.ContextJson, "configuredTimeoutSeconds", "ConfiguredTimeoutSeconds"),
            DeriveHedgeState(action),
            TryFindString(riskEvent.ContextJson, "mitigationResult", "MitigationResult", "result", "Result") ?? action,
            "risk_event");
    }

    private static bool IsUnhedgedExposureEvent(RiskEventRecord riskEvent)
        => ContainsIgnoreCase(riskEvent.Code, "UNHEDGED")
            || ContainsIgnoreCase(riskEvent.Message, "unhedged")
            || TryFindString(riskEvent.ContextJson, "hedgeTokenId", "HedgeTokenId") is not null;

    private static string DeriveLimitName(string code)
        => code.StartsWith("RISK_", StringComparison.OrdinalIgnoreCase)
            ? code[5..].Replace('_', ' ').ToLowerInvariant()
            : code.Replace('_', ' ').ToLowerInvariant();

    private static string DeriveSelectedAction(string code)
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

        return ContainsIgnoreCase(code, "KILL") ? "KillSwitch" : "Block";
    }

    private static string DeriveHedgeState(string action)
    {
        if (ContainsIgnoreCase(action, "ForceHedge"))
        {
            return "HedgeAttempted";
        }

        if (ContainsIgnoreCase(action, "Exit"))
        {
            return "ExitAttempted";
        }

        if (ContainsIgnoreCase(action, "Cancel"))
        {
            return "OrdersCancelled";
        }

        return ContainsIgnoreCase(action, "Log") ? "OpenLogged" : "Unknown";
    }

    private static bool ContainsIgnoreCase(string? value, string expected)
        => value?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true;

    private static bool Matches(string? expected, string actual)
        => string.IsNullOrWhiteSpace(expected)
            || string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);

    private static Guid? TryGuidFromReference(string reference)
    {
        var value = reference.Split('/').LastOrDefault();
        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string? TryFindString(string? json, params string[] propertyNames)
        => FindStrings(json, propertyNames).FirstOrDefault();

    private static decimal? TryFindDecimal(string? json, params string[] propertyNames)
        => decimal.TryParse(TryFindString(json, propertyNames), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static double? TryFindDouble(string? json, params string[] propertyNames)
        => double.TryParse(TryFindString(json, propertyNames), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static DateTimeOffset? TryFindDateTimeOffset(string? json, params string[] propertyNames)
        => DateTimeOffset.TryParse(TryFindString(json, propertyNames), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
            ? value
            : null;

    private static IReadOnlyList<Guid> FindGuids(string? json, params string[] propertyNames)
        => FindStrings(json, propertyNames)
            .Select(value => Guid.TryParse(value, out var id) ? id : (Guid?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();

    private static IReadOnlyList<string> FindStrings(string? json, params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var values = new List<string>();
            CollectStrings(document.RootElement, propertyNames, values);
            return values;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static void CollectStrings(JsonElement element, IReadOnlyList<string> propertyNames, List<string> values)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (propertyNames.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    AddJsonValue(property.Value, values);
                }

                CollectStrings(property.Value, propertyNames, values);
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectStrings(item, propertyNames, values);
            }
        }
    }

    private static void AddJsonValue(JsonElement element, List<string> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                values.Add(element.GetString() ?? string.Empty);
                break;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                values.Add(element.GetRawText());
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AddJsonValue(item, values);
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    AddJsonValue(property.Value, values);
                }
                break;
            case JsonValueKind.Undefined:
            case JsonValueKind.Null:
            default:
                break;
        }
    }
}

internal static class RiskDrilldownEnumerableExtensions
{
    public static IEnumerable<TResult> SelectNotNull<TSource, TResult>(
        this IEnumerable<TSource> source,
        Func<TSource, TResult?> selector)
        where TResult : class
    {
        foreach (var item in source)
        {
            var result = selector(item);
            if (result is not null)
            {
                yield return result;
            }
        }
    }
}
