using System.Globalization;
using System.Text.Json;
using Autotrade.Strategy.Application.Decisions;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Controllers;

[ApiController]
[Route("api/strategy-decisions")]
public sealed class StrategyDecisionsController(
    IStrategyDecisionQueryService queryService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<StrategyDecisionListResponse>> Query(
        [FromQuery] string? strategyId,
        [FromQuery] string? marketId,
        [FromQuery] string? action,
        [FromQuery] string? correlationId,
        [FromQuery] Guid? runSessionId,
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var normalizedLimit = Math.Clamp(limit ?? 200, 1, 500);
        var query = new StrategyDecisionQuery(
            strategyId,
            marketId,
            fromUtc,
            toUtc,
            normalizedLimit,
            action,
            correlationId,
            runSessionId);

        var decisions = await queryService
            .QueryRecordsAsync(query, cancellationToken)
            .ConfigureAwait(false);

        return Ok(new StrategyDecisionListResponse(
            DateTimeOffset.UtcNow,
            decisions.Count,
            normalizedLimit,
            decisions.Select(ToSummary).ToArray()));
    }

    [HttpGet("{decisionId:guid}")]
    public async Task<ActionResult<StrategyDecisionDetailResponse>> GetDetail(
        Guid decisionId,
        CancellationToken cancellationToken)
    {
        var decision = await queryService.GetAsync(decisionId, cancellationToken).ConfigureAwait(false);
        if (decision is null)
        {
            return NotFound();
        }

        return Ok(ToDetail(decision));
    }

    private static StrategyDecisionSummaryDto ToSummary(StrategyDecisionRecord decision)
        => new(
            decision.DecisionId,
            decision.StrategyId,
            decision.Action,
            decision.Reason,
            decision.MarketId,
            decision.TimestampUtc,
            decision.ConfigVersion,
            decision.CorrelationId,
            decision.ExecutionMode,
            decision.RunSessionId);

    private static StrategyDecisionDetailResponse ToDetail(StrategyDecisionRecord decision)
        => new(
            decision.DecisionId,
            decision.StrategyId,
            decision.Action,
            decision.Reason,
            decision.MarketId,
            decision.TimestampUtc,
            decision.ConfigVersion,
            decision.CorrelationId,
            decision.ExecutionMode,
            decision.RunSessionId,
            BuildReasonChain(decision.ContextJson));

    private static StrategyDecisionReasonChainDto BuildReasonChain(string? contextJson)
    {
        if (string.IsNullOrWhiteSpace(contextJson))
        {
            return StrategyDecisionReasonChainDto.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(contextJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return StrategyDecisionReasonChainDto.Empty with { RawContextJson = contextJson };
            }

            return new StrategyDecisionReasonChainDto(
                ExtractObject(root, "signalInputs", "inputs", "marketObservation", "snapshot"),
                ExtractObject(root, "thresholds", "parameters", "limits"),
                ExtractRiskVerdict(root),
                ExtractOrderReferences(root),
                contextJson);
        }
        catch (JsonException)
        {
            return StrategyDecisionReasonChainDto.Empty with { RawContextJson = contextJson };
        }
    }

    private static IReadOnlyDictionary<string, string> ExtractObject(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            return element
                .EnumerateObject()
                .ToDictionary(
                    item => item.Name,
                    item => JsonValue(item.Value),
                    StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static StrategyDecisionRiskVerdictDto? ExtractRiskVerdict(JsonElement root)
    {
        var element = default(JsonElement);
        if (root.TryGetProperty("riskVerdict", out var riskVerdict))
        {
            element = riskVerdict;
        }
        else if (root.TryGetProperty("risk", out var risk))
        {
            element = risk;
        }
        else
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return new StrategyDecisionRiskVerdictDto(null, null, JsonValue(element));
        }

        return new StrategyDecisionRiskVerdictDto(
            TryGetBoolean(element, "allowed"),
            TryGetString(element, "code") ?? TryGetString(element, "reasonCode"),
            TryGetString(element, "message") ?? TryGetString(element, "reason"));
    }

    private static IReadOnlyList<StrategyDecisionOrderReferenceDto> ExtractOrderReferences(JsonElement root)
    {
        var element = default(JsonElement);
        if (root.TryGetProperty("orderReferences", out var orderReferences))
        {
            element = orderReferences;
        }
        else if (root.TryGetProperty("orders", out var orders))
        {
            element = orders;
        }
        else if (root.TryGetProperty("orderIntents", out var orderIntents))
        {
            element = orderIntents;
        }
        else
        {
            return Array.Empty<StrategyDecisionOrderReferenceDto>();
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<StrategyDecisionOrderReferenceDto>();
        }

        return element
            .EnumerateArray()
            .Select(ToOrderReference)
            .ToArray();
    }

    private static StrategyDecisionOrderReferenceDto ToOrderReference(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return new StrategyDecisionOrderReferenceDto(element.GetString(), null, null, null, null, null, null, null);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return new StrategyDecisionOrderReferenceDto(JsonValue(element), null, null, null, null, null, null, null);
        }

        return new StrategyDecisionOrderReferenceDto(
            TryGetString(element, "clientOrderId") ?? TryGetString(element, "orderId"),
            TryGetString(element, "marketId"),
            TryGetString(element, "tokenId"),
            TryGetString(element, "side"),
            TryGetString(element, "outcome"),
            TryGetString(element, "price"),
            TryGetString(element, "quantity"),
            TryGetString(element, "status"));
    }

    private static bool? TryGetBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static string? TryGetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) ? JsonValue(value) : null;

    private static string JsonValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number when element.TryGetDecimal(out var value) => value.ToString(CultureInfo.InvariantCulture),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            _ => element.GetRawText()
        };
}

public sealed record StrategyDecisionListResponse(
    DateTimeOffset TimestampUtc,
    int Count,
    int Limit,
    IReadOnlyList<StrategyDecisionSummaryDto> Decisions);

public sealed record StrategyDecisionSummaryDto(
    Guid DecisionId,
    string StrategyId,
    string Action,
    string Reason,
    string? MarketId,
    DateTimeOffset CreatedAtUtc,
    string ConfigVersion,
    string? CorrelationId,
    string? ExecutionMode,
    Guid? RunSessionId);

public sealed record StrategyDecisionDetailResponse(
    Guid DecisionId,
    string StrategyId,
    string Action,
    string Reason,
    string? MarketId,
    DateTimeOffset CreatedAtUtc,
    string ConfigVersion,
    string? CorrelationId,
    string? ExecutionMode,
    Guid? RunSessionId,
    StrategyDecisionReasonChainDto ReasonChain);

public sealed record StrategyDecisionReasonChainDto(
    IReadOnlyDictionary<string, string> SignalInputs,
    IReadOnlyDictionary<string, string> Thresholds,
    StrategyDecisionRiskVerdictDto? RiskVerdict,
    IReadOnlyList<StrategyDecisionOrderReferenceDto> OrderReferences,
    string? RawContextJson)
{
    public static StrategyDecisionReasonChainDto Empty { get; } = new(
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        null,
        Array.Empty<StrategyDecisionOrderReferenceDto>(),
        null);
}

public sealed record StrategyDecisionRiskVerdictDto(
    bool? Allowed,
    string? Code,
    string? Message);

public sealed record StrategyDecisionOrderReferenceDto(
    string? ClientOrderId,
    string? MarketId,
    string? TokenId,
    string? Side,
    string? Outcome,
    string? Price,
    string? Quantity,
    string? Status);
