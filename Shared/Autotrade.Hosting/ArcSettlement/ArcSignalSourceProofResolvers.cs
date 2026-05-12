using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using Autotrade.ArcSettlement.Application.Contract.Configuration;
using Autotrade.ArcSettlement.Application.Contract.Proofs;
using Autotrade.ArcSettlement.Application.Contract.Signals;
using Autotrade.ArcSettlement.Application.Proofs;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using Autotrade.Strategy.Application.Decisions;
using Microsoft.Extensions.Options;

namespace Autotrade.Hosting.ArcSettlement;

public sealed class ArcOpportunitySignalProofResolver(
    IOpportunityQueryService opportunities,
    IArcProofHashService hashService,
    IOptionsMonitor<ArcSettlementOptions> options) : IArcSignalProofSourceResolver
{
    public ArcProofSourceKind SourceKind => ArcProofSourceKind.Opportunity;

    public async Task<ArcSignalSourceProofResolution> ResolveAsync(
        PublishArcSignalSourceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Guid.TryParse(request.SourceId, out var opportunityId) || opportunityId == Guid.Empty)
        {
            throw new ArcSignalSourceResolutionException(
                "INVALID_SOURCE_ID",
                "Opportunity source id must be a non-empty GUID.");
        }

        var opportunity = await opportunities.GetOpportunityAsync(opportunityId, cancellationToken)
            .ConfigureAwait(false);
        if (opportunity is null)
        {
            throw new ArcSignalSourceResolutionException(
                "SOURCE_NOT_FOUND",
                $"Opportunity source was not found: {request.SourceId}.");
        }

        var policy = DeserializePolicy(opportunity);
        var evidence = await opportunities.GetEvidenceAsync(opportunity.Id, cancellationToken)
            .ConfigureAwait(false);
        var proofOptions = options.CurrentValue.SignalProof;
        var agentAddress = ArcSignalSourceProofJson.RequireAgentAddress(proofOptions.AgentAddress);
        var sourcePolicyHash = ArcSignalSourceProofJson.HashStable(policy);
        var evidenceIds = ResolveEvidenceIds(opportunity, policy, evidence);
        var strategyId = string.IsNullOrWhiteSpace(proofOptions.OpportunityStrategyId)
            ? "llm_opportunity"
            : proofOptions.OpportunityStrategyId;
        var venue = string.IsNullOrWhiteSpace(proofOptions.Venue)
            ? "polymarket"
            : proofOptions.Venue;

        var riskEnvelope = new ArcRiskEnvelopeDocument(
            "arc-risk-envelope.v1",
            strategyId,
            opportunity.MarketId,
            ArcProofExecutionMode.Paper,
            policy.MaxNotional,
            ResolveRiskTier(proofOptions.DefaultRiskTier),
            KillSwitchActive: false,
            LiveArmed: false,
            ConstraintIds:
            [
                "opportunity_status_approved_or_published",
                "opportunity_valid_until",
                "compiled_policy_max_notional",
                "compiled_policy_entry_max_price",
                "compiled_policy_max_spread"
            ]);

        var opportunityHash = ArcSignalSourceProofJson.HashStable(
            new OpportunityProofMaterial(
                opportunity.Id.ToString("D"),
                opportunity.ResearchRunId.ToString("D"),
                opportunity.MarketId,
                opportunity.Outcome.ToString(),
                opportunity.FairProbability,
                opportunity.Confidence,
                opportunity.Edge,
                opportunity.Status.ToString(),
                opportunity.ValidUntilUtc,
                sourcePolicyHash));

        var reasoningHash = ArcSignalSourceProofJson.HashStable(
            new OpportunityReasoningMaterial(
                opportunity.Id.ToString("D"),
                opportunity.Reason,
                opportunity.ScoreJson,
                evidence
                    .OrderBy(item => item.Id)
                    .Select(item => new EvidenceReasoningMaterial(
                        item.Id.ToString("D"),
                        item.SourceKind.ToString(),
                        item.SourceName,
                        item.ContentHash,
                        item.ObservedAtUtc))
                    .ToArray()));

        var signalProof = new ArcStrategySignalProofDocument(
            "arc-strategy-signal-proof.v1",
            agentAddress,
            ArcProofSourceKind.Opportunity,
            opportunity.Id.ToString("D"),
            strategyId,
            opportunity.MarketId,
            venue,
            opportunity.CreatedAtUtc,
            sourcePolicyHash,
            evidenceIds,
            opportunityHash,
            reasoningHash,
            ArcSignalSourceProofJson.NormalizeBytes32Hash(hashService.HashRiskEnvelope(riskEnvelope)),
            opportunity.Edge * 10_000m,
            policy.MaxNotional,
            opportunity.ValidUntilUtc);

        return new ArcSignalSourceProofResolution(
            signalProof,
            MapReviewStatus(opportunity.Status),
            sourcePolicyHash);
    }

    private static CompiledOpportunityPolicy DeserializePolicy(MarketOpportunityDto opportunity)
    {
        try
        {
            return JsonSerializer.Deserialize<CompiledOpportunityPolicy>(
                    opportunity.CompiledPolicyJson,
                    ArcSignalSourceProofJson.SourceJsonOptions)
                ?? throw new JsonException("Compiled policy JSON returned null.");
        }
        catch (JsonException)
        {
            throw new ArcSignalSourceResolutionException(
                "SOURCE_POLICY_INVALID",
                $"Opportunity {opportunity.Id} has invalid compiled policy JSON.");
        }
    }

    private static IReadOnlyList<string> ResolveEvidenceIds(
        MarketOpportunityDto opportunity,
        CompiledOpportunityPolicy policy,
        IReadOnlyList<EvidenceItemDto> evidence)
    {
        var ids = policy.EvidenceIds
            .Where(id => id != Guid.Empty)
            .Select(id => id.ToString("D"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (ids.Length > 0)
        {
            return ids;
        }

        var opportunityEvidenceIds = ArcSignalSourceProofJson.ReadGuidArray(opportunity.EvidenceIdsJson)
            .Select(id => id.ToString("D"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (opportunityEvidenceIds.Length > 0)
        {
            return opportunityEvidenceIds;
        }

        return evidence
            .Select(item => item.Id.ToString("D"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ArcSignalSourceReviewStatus MapReviewStatus(OpportunityStatus status)
        => status switch
        {
            OpportunityStatus.Candidate => ArcSignalSourceReviewStatus.Candidate,
            OpportunityStatus.NeedsReview => ArcSignalSourceReviewStatus.NeedsReview,
            OpportunityStatus.Approved => ArcSignalSourceReviewStatus.Approved,
            OpportunityStatus.Published => ArcSignalSourceReviewStatus.Published,
            OpportunityStatus.Rejected => ArcSignalSourceReviewStatus.Rejected,
            OpportunityStatus.Expired => ArcSignalSourceReviewStatus.Expired,
            _ => ArcSignalSourceReviewStatus.Unknown
        };

    private static string ResolveRiskTier(string value)
        => string.IsNullOrWhiteSpace(value) ? "paper" : value;

    private sealed record OpportunityProofMaterial(
        string OpportunityId,
        string ResearchRunId,
        string MarketId,
        string Outcome,
        decimal FairProbability,
        decimal Confidence,
        decimal Edge,
        string Status,
        DateTimeOffset ValidUntilUtc,
        string SourcePolicyHash);

    private sealed record OpportunityReasoningMaterial(
        string OpportunityId,
        string Reason,
        string ScoreJson,
        IReadOnlyList<EvidenceReasoningMaterial> Evidence);

    private sealed record EvidenceReasoningMaterial(
        string EvidenceId,
        string SourceKind,
        string SourceName,
        string ContentHash,
        DateTimeOffset ObservedAtUtc);
}

public sealed class ArcStrategyDecisionSignalProofResolver(
    IStrategyDecisionQueryService decisions,
    IOptionsMonitor<ArcSettlementOptions> options) : IArcSignalProofSourceResolver
{
    public ArcProofSourceKind SourceKind => ArcProofSourceKind.StrategyDecision;

    public async Task<ArcSignalSourceProofResolution> ResolveAsync(
        PublishArcSignalSourceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Guid.TryParse(request.SourceId, out var decisionId) || decisionId == Guid.Empty)
        {
            throw new ArcSignalSourceResolutionException(
                "INVALID_SOURCE_ID",
                "Strategy decision source id must be a non-empty GUID.");
        }

        var decision = await decisions.GetAsync(decisionId, cancellationToken).ConfigureAwait(false);
        if (decision is null)
        {
            throw new ArcSignalSourceResolutionException(
                "SOURCE_NOT_FOUND",
                $"Strategy decision source was not found: {request.SourceId}.");
        }

        if (string.IsNullOrWhiteSpace(decision.StrategyId)
            || string.IsNullOrWhiteSpace(decision.MarketId)
            || string.IsNullOrWhiteSpace(decision.ContextJson))
        {
            throw new ArcSignalSourceResolutionException(
                "DECISION_CONTEXT_INCOMPLETE",
                "Strategy decision must include strategy id, market id, and context JSON before it can become an Arc signal.");
        }

        using var contextDocument = ParseDecisionContext(decision);
        var contextRoot = contextDocument.RootElement;
        var proofOptions = options.CurrentValue.SignalProof;
        var agentAddress = ArcSignalSourceProofJson.RequireAgentAddress(proofOptions.AgentAddress);
        var venue = string.IsNullOrWhiteSpace(proofOptions.Venue)
            ? "polymarket"
            : proofOptions.Venue;

        var riskEnvelopeHash = ResolveRiskEnvelopeHash(contextRoot);
        var expectedEdgeBps = ResolveExpectedEdgeBps(contextRoot);
        var maxNotionalUsdc = ResolveMaxNotionalUsdc(contextRoot);
        var validUntilUtc = ArcSignalSourceProofJson.TryGetDateTimeOffset(
                contextRoot,
                out var parsedValidUntil,
                "validUntilUtc",
                "expiresAtUtc",
                "signalValidUntilUtc")
            ? parsedValidUntil
            : decision.TimestampUtc.AddMinutes(Math.Max(1, proofOptions.DecisionValidForMinutes));
        var evidenceIds = ArcSignalSourceProofJson.TryGetStringArray(contextRoot, out var parsedEvidenceIds, "evidenceIds")
            ? parsedEvidenceIds
            : [];
        var opportunityHash = ArcSignalSourceProofJson.HashStable(
            new StrategyDecisionProofMaterial(
                decision.DecisionId.ToString("D"),
                decision.StrategyId,
                decision.Action,
                decision.Reason,
                decision.MarketId!,
                decision.TimestampUtc,
                ArcSignalSourceProofJson.NormalizeJsonElement(contextRoot)));
        var reasoningHash = ArcSignalSourceProofJson.HashStable(
            new StrategyDecisionReasoningMaterial(
                decision.DecisionId.ToString("D"),
                decision.Reason,
                decision.CorrelationId,
                ArcSignalSourceProofJson.NormalizeJsonElement(contextRoot)));
        var sourcePolicyHash = TryResolveSourcePolicyHash(contextRoot);

        var signalProof = new ArcStrategySignalProofDocument(
            "arc-strategy-signal-proof.v1",
            agentAddress,
            ArcProofSourceKind.StrategyDecision,
            decision.DecisionId.ToString("D"),
            decision.StrategyId,
            decision.MarketId!,
            venue,
            decision.TimestampUtc,
            decision.ConfigVersion,
            evidenceIds,
            opportunityHash,
            reasoningHash,
            riskEnvelopeHash,
            expectedEdgeBps,
            maxNotionalUsdc,
            validUntilUtc);

        return new ArcSignalSourceProofResolution(
            signalProof,
            ArcSignalSourceReviewStatus.Approved,
            sourcePolicyHash);
    }

    private static JsonDocument ParseDecisionContext(StrategyDecisionRecord decision)
    {
        try
        {
            var document = JsonDocument.Parse(decision.ContextJson!);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                throw new ArcSignalSourceResolutionException(
                    "DECISION_CONTEXT_INCOMPLETE",
                    "Strategy decision context JSON must be a JSON object.");
            }

            return document;
        }
        catch (JsonException)
        {
            throw new ArcSignalSourceResolutionException(
                "DECISION_CONTEXT_INVALID",
                $"Strategy decision {decision.DecisionId} has invalid context JSON.");
        }
    }

    private static string ResolveRiskEnvelopeHash(JsonElement contextRoot)
    {
        if (ArcSignalSourceProofJson.TryGetString(
                contextRoot,
                out var riskEnvelopeHash,
                "riskEnvelopeHash",
                "riskHash"))
        {
            return ArcSignalSourceProofJson.NormalizeBytes32Hash(riskEnvelopeHash);
        }

        if (!ArcSignalSourceProofJson.TryGetProperty(contextRoot, out var riskEnvelope, "riskEnvelope"))
        {
            throw new ArcSignalSourceResolutionException(
                "DECISION_RISK_ENVELOPE_MISSING",
                "Strategy decision context must include riskEnvelope or riskEnvelopeHash.");
        }

        return ArcSignalSourceProofJson.HashJsonElement(riskEnvelope);
    }

    private static decimal ResolveExpectedEdgeBps(JsonElement contextRoot)
    {
        if (ArcSignalSourceProofJson.TryGetDecimal(
                contextRoot,
                out var expectedEdgeBps,
                "expectedEdgeBps",
                "edgeBps"))
        {
            return expectedEdgeBps;
        }

        if (ArcSignalSourceProofJson.TryGetDecimal(
                contextRoot,
                out var expectedEdge,
                "expectedEdge",
                "opportunityEdge",
                "edge"))
        {
            return expectedEdge * 10_000m;
        }

        throw new ArcSignalSourceResolutionException(
            "DECISION_EXPECTED_EDGE_MISSING",
            "Strategy decision context must include expectedEdgeBps, edgeBps, expectedEdge, opportunityEdge, or edge.");
    }

    private static decimal ResolveMaxNotionalUsdc(JsonElement contextRoot)
    {
        if (ArcSignalSourceProofJson.TryGetDecimal(
                contextRoot,
                out var maxNotional,
                "maxNotionalUsdc",
                "maxNotional"))
        {
            return maxNotional;
        }

        if (ArcSignalSourceProofJson.TryGetProperty(contextRoot, out var riskEnvelope, "riskEnvelope")
            && ArcSignalSourceProofJson.TryGetDecimal(
                riskEnvelope,
                out var riskMaxNotional,
                "maxNotionalUsdc",
                "maxNotional"))
        {
            return riskMaxNotional;
        }

        throw new ArcSignalSourceResolutionException(
            "DECISION_MAX_NOTIONAL_MISSING",
            "Strategy decision context must include maxNotionalUsdc or riskEnvelope.maxNotionalUsdc.");
    }

    private static string? TryResolveSourcePolicyHash(JsonElement contextRoot)
    {
        if (!ArcSignalSourceProofJson.TryGetString(
                contextRoot,
                out var sourcePolicyHash,
                "sourcePolicyHash",
                "policyHash",
                "compiledPolicyHash"))
        {
            return null;
        }

        return ArcSignalSourceProofJson.NormalizeBytes32Hash(sourcePolicyHash);
    }

    private sealed record StrategyDecisionProofMaterial(
        string DecisionId,
        string StrategyId,
        string Action,
        string Reason,
        string MarketId,
        DateTimeOffset TimestampUtc,
        object? Context);

    private sealed record StrategyDecisionReasoningMaterial(
        string DecisionId,
        string Reason,
        string? CorrelationId,
        object? Context);
}

internal static class ArcSignalSourceProofJson
{
    public static JsonSerializerOptions SourceJsonOptions { get; } = CreateSourceJsonOptions();

    public static string RequireAgentAddress(string value)
    {
        if (IsNonZeroEvmAddress(value))
        {
            return value.ToLowerInvariant();
        }

        throw new ArcSignalSourceResolutionException(
            "ARC_AGENT_ADDRESS_MISSING",
            "ArcSettlement:SignalProof:AgentAddress must be configured as a non-zero EVM address.");
    }

    public static string NormalizeBytes32Hash(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArcSignalSourceResolutionException(
                "INVALID_PROOF_HASH",
                "Proof hash value cannot be empty.");
        }

        var normalized = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? value.ToLowerInvariant()
            : $"0x{value.ToLowerInvariant()}";
        if (normalized.Length == 66 && normalized[2..].All(Uri.IsHexDigit))
        {
            return normalized;
        }

        throw new ArcSignalSourceResolutionException(
            "INVALID_PROOF_HASH",
            "Proof hash value must be a 32-byte hex string.");
    }

    public static string HashStable<T>(T value)
    {
        var json = value is null
            ? "null"
            : JsonSerializer.Serialize(value, value.GetType(), ArcProofJson.StableSerializerOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return $"0x{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    public static string HashJsonElement(JsonElement element)
        => HashStable(NormalizeJsonElement(element));

    public static object? NormalizeJsonElement(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => element
                .EnumerateObject()
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .ToDictionary(
                    property => property.Name,
                    property => NormalizeJsonElement(property.Value),
                    StringComparer.Ordinal),
            JsonValueKind.Array => element
                .EnumerateArray()
                .Select(NormalizeJsonElement)
                .ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetDecimal(out var value) => value,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };

    public static bool TryGetProperty(JsonElement root, out JsonElement value, params string[] names)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    public static bool TryGetString(JsonElement root, out string value, params string[] names)
    {
        if (TryGetProperty(root, out var property, names)
            && property.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(property.GetString()))
        {
            value = property.GetString()!;
            return true;
        }

        value = string.Empty;
        return false;
    }

    public static bool TryGetDateTimeOffset(JsonElement root, out DateTimeOffset value, params string[] names)
    {
        if (TryGetString(root, out var text, names)
            && DateTimeOffset.TryParse(text, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    public static bool TryGetDecimal(JsonElement root, out decimal value, params string[] names)
    {
        if (!TryGetProperty(root, out var property, names))
        {
            value = default;
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out value))
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.String
            && decimal.TryParse(
                property.GetString(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    public static bool TryGetStringArray(JsonElement root, out IReadOnlyList<string> values, params string[] names)
    {
        if (TryGetProperty(root, out var property, names)
            && property.ValueKind == JsonValueKind.Array)
        {
            values = property
                .EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return true;
        }

        values = [];
        return false;
    }

    public static IReadOnlyList<Guid> ReadGuidArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Guid[]>(json, SourceJsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static JsonSerializerOptions CreateSourceJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    private static bool IsNonZeroEvmAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length != 42
            || !value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var body = value[2..];
        return body.All(Uri.IsHexDigit) && !body.All(character => character == '0');
    }
}
