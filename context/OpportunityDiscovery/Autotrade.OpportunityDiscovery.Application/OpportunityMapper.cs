using System.Text.Json;
using System.Text.Json.Serialization;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.OpportunityDiscovery.Domain.Entities;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.OpportunityDiscovery.Application;

internal static class OpportunityMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static ResearchRunDto ToDto(ResearchRun run)
        => new(
            run.Id,
            run.Trigger,
            run.MarketUniverseJson,
            run.Status,
            run.EvidenceCount,
            run.OpportunityCount,
            run.ErrorMessage,
            run.CreatedAtUtc,
            run.UpdatedAtUtc);

    public static EvidenceItemDto ToDto(EvidenceItem evidence)
        => new(
            evidence.Id,
            evidence.ResearchRunId,
            evidence.SourceKind,
            evidence.SourceName,
            evidence.Url,
            evidence.Title,
            evidence.Summary,
            evidence.PublishedAtUtc,
            evidence.ObservedAtUtc,
            evidence.ContentHash,
            evidence.SourceQuality);

    public static MarketOpportunityDto ToDto(MarketOpportunity opportunity)
        => new(
            opportunity.Id,
            opportunity.ResearchRunId,
            opportunity.MarketId,
            (OutcomeSide)opportunity.Outcome,
            opportunity.FairProbability,
            opportunity.Confidence,
            opportunity.Edge,
            opportunity.Status,
            opportunity.ValidUntilUtc,
            opportunity.Reason,
            opportunity.EvidenceIdsJson,
            opportunity.ScoreJson,
            opportunity.CompiledPolicyJson,
            opportunity.CreatedAtUtc,
            opportunity.UpdatedAtUtc);

    public static PublishedOpportunityDto ToPublishedDto(MarketOpportunity opportunity)
    {
        var policy = JsonSerializer.Deserialize<CompiledOpportunityPolicy>(
                opportunity.CompiledPolicyJson,
                JsonOptions)
            ?? throw new InvalidOperationException($"Opportunity {opportunity.Id} has an invalid compiled policy.");

        return new PublishedOpportunityDto(
            opportunity.Id,
            opportunity.ResearchRunId,
            opportunity.MarketId,
            (OutcomeSide)opportunity.Outcome,
            opportunity.Edge,
            opportunity.ValidUntilUtc,
            policy.EvidenceIds,
            policy);
    }
}
