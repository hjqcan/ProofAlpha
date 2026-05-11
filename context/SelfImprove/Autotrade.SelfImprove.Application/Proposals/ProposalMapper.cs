using System.Text.Json;
using Autotrade.SelfImprove.Application.Contract;
using Autotrade.SelfImprove.Application.Contract.Proposals;
using Autotrade.SelfImprove.Domain.Entities;

namespace Autotrade.SelfImprove.Application.Proposals;

public static class ProposalMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ImprovementProposal ToEntity(
        this ImprovementProposalDocument document,
        Guid runId,
        string strategyId,
        bool requiresManualReview)
    {
        return new ImprovementProposal(
            runId,
            strategyId,
            document.Kind,
            document.RiskLevel,
            document.Title,
            document.Rationale,
            JsonSerializer.Serialize(document.Evidence, JsonOptions),
            JsonSerializer.Serialize(new { text = document.ExpectedImpact }, JsonOptions),
            JsonSerializer.Serialize(document.RollbackConditions, JsonOptions),
            document.ParameterPatches.Count == 0 ? null : JsonSerializer.Serialize(document.ParameterPatches, JsonOptions),
            document.GeneratedStrategy is null ? null : JsonSerializer.Serialize(document.GeneratedStrategy, JsonOptions),
            requiresManualReview,
            DateTimeOffset.UtcNow);
    }

    public static ImprovementProposalDto ToDto(this ImprovementProposal proposal)
    {
        return new ImprovementProposalDto(
            proposal.Id,
            proposal.RunId,
            proposal.StrategyId,
            proposal.Kind,
            proposal.Status,
            proposal.RiskLevel,
            proposal.Title,
            proposal.Rationale,
            proposal.EvidenceJson,
            proposal.ExpectedImpactJson,
            proposal.RollbackConditionsJson,
            proposal.ParameterPatchJson,
            proposal.CodeGenerationSpecJson,
            proposal.RequiresManualReview,
            proposal.CreatedAtUtc);
    }

    public static ImprovementRunDto ToDto(this ImprovementRun run)
    {
        return new ImprovementRunDto(
            run.Id,
            run.StrategyId,
            run.MarketId,
            run.WindowStartUtc,
            run.WindowEndUtc,
            run.Trigger,
            run.Status,
            run.EpisodeId,
            run.ProposalCount,
            run.ErrorMessage,
            run.CreatedAtUtc,
            run.UpdatedAtUtc);
    }

    public static PatchOutcomeDto ToDto(this PatchOutcome outcome)
    {
        return new PatchOutcomeDto(
            outcome.Id,
            outcome.ProposalId,
            outcome.StrategyId,
            outcome.Status,
            outcome.DiffJson,
            outcome.RollbackJson,
            outcome.Message,
            outcome.CreatedAtUtc);
    }
}
