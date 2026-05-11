using Autotrade.SelfImprove.Application.Contract.Proposals;
using Autotrade.SelfImprove.Domain.Shared.Enums;

namespace Autotrade.SelfImprove.Application.Proposals;

public interface IProposalValidator
{
    ProposalValidationResult Validate(ImprovementProposalDocument proposal);
}

public sealed class ProposalValidator : IProposalValidator
{
    private static readonly string[] ForbiddenPathTokens =
    {
        "apikey",
        "api_key",
        "secret",
        "privatekey",
        "private_key",
        "passphrase",
        "connectionstrings",
        "execution:mode",
        "compliance:",
        "riskcontrol:",
        "killswitch",
        "liveautoapplyenabled"
    };

    public ProposalValidationResult Validate(ImprovementProposalDocument proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);

        var errors = new List<string>();
        var warnings = new List<string>();
        var requiresManualReview = proposal.Kind == ProposalKind.ManualInvestigation
            || proposal.RiskLevel is ImprovementRiskLevel.High or ImprovementRiskLevel.Critical;

        if (string.IsNullOrWhiteSpace(proposal.Title))
        {
            errors.Add("Proposal title is required.");
        }

        if (proposal.Kind != ProposalKind.ManualInvestigation && proposal.Evidence.Count == 0)
        {
            requiresManualReview = true;
            warnings.Add("Proposal has no evidence ids and requires manual review.");
        }

        if (proposal.Kind == ProposalKind.ParameterPatch && proposal.ParameterPatches.Count == 0)
        {
            errors.Add("Parameter patch proposal must include at least one parameter patch.");
        }

        foreach (var patch in proposal.ParameterPatches)
        {
            ValidatePatch(patch, errors, warnings);
        }

        if (proposal.Kind == ProposalKind.GeneratedStrategy && proposal.GeneratedStrategy is null)
        {
            errors.Add("Generated strategy proposal must include generatedStrategy.");
        }

        if (proposal.GeneratedStrategy is not null)
        {
            ValidateGeneratedStrategySpec(proposal.GeneratedStrategy, errors);
        }

        return new ProposalValidationResult(errors.Count == 0, requiresManualReview, errors, warnings);
    }

    private static void ValidatePatch(
        ParameterPatchSpec patch,
        ICollection<string> errors,
        ICollection<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(patch.Path))
        {
            errors.Add("Parameter patch path cannot be empty.");
            return;
        }

        if (!patch.Path.StartsWith("Strategies:", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{patch.Path}: SelfImprove parameter patches are restricted to Strategies:* paths.");
        }

        var normalized = patch.Path.ToLowerInvariant();
        if (ForbiddenPathTokens.Any(token => normalized.Contains(token, StringComparison.Ordinal)))
        {
            errors.Add($"{patch.Path}: Path is permanently forbidden.");
        }

        if (patch.Path.EndsWith(":Enabled", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{patch.Path}: Strategy Enabled flags cannot be toggled by SelfImprove.");
        }

        if (patch.MaxRelativeChange is > 0.5m)
        {
            errors.Add($"{patch.Path}: MaxRelativeChange above 0.5 is too large for automatic patching.");
        }
        else if (patch.MaxRelativeChange is null)
        {
            warnings.Add($"{patch.Path}: No maxRelativeChange was supplied.");
        }

        try
        {
            _ = System.Text.Json.JsonDocument.Parse(patch.ValueJson);
        }
        catch (System.Text.Json.JsonException ex)
        {
            errors.Add($"{patch.Path}: valueJson is not valid JSON. {ex.Message}");
        }
    }

    private static void ValidateGeneratedStrategySpec(GeneratedStrategySpec spec, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(spec.StrategyId))
        {
            errors.Add("Generated strategy id is required.");
        }

        if (string.IsNullOrWhiteSpace(spec.PythonModule))
        {
            errors.Add("Generated strategy pythonModule is required.");
        }

        if (string.IsNullOrWhiteSpace(spec.ManifestJson))
        {
            errors.Add("Generated strategy manifestJson is required.");
        }

        if (string.IsNullOrWhiteSpace(spec.UnitTests))
        {
            errors.Add("Generated strategy unitTests are required.");
        }

        if (string.IsNullOrWhiteSpace(spec.ReplaySpecJson))
        {
            errors.Add("Generated strategy replaySpecJson is required.");
        }

        if (string.IsNullOrWhiteSpace(spec.RiskEnvelopeJson))
        {
            errors.Add("Generated strategy riskEnvelopeJson is required.");
        }
    }
}
