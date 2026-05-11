using Autotrade.SelfImprove.Application.Contract.Proposals;
using Autotrade.SelfImprove.Application.Proposals;
using Autotrade.SelfImprove.Domain.Shared.Enums;

namespace Autotrade.SelfImprove.Tests.Proposals;

public sealed class ProposalValidatorTests
{
    [Fact]
    public void Validate_RequiresManualReview_WhenProposalHasNoEvidence()
    {
        var validator = new ProposalValidator();
        var proposal = new ImprovementProposalDocument(
            ProposalKind.ParameterPatch,
            ImprovementRiskLevel.Low,
            "Tighten spread",
            "Observed repeated adverse fills.",
            Array.Empty<EvidenceRef>(),
            "Lower reject rate",
            new[] { "reject rate increases" },
            new[]
            {
                new ParameterPatchSpec(
                    "Strategies:LiquidityPulse:MaxSpread",
                    "0.04",
                    "Reduce entries when spread is too wide",
                    0.2m)
            },
            null);

        var result = validator.Validate(proposal);

        Assert.True(result.IsValid);
        Assert.True(result.RequiresManualReview);
        Assert.Contains(result.Warnings, warning => warning.Contains("no evidence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RejectsForbiddenConfigPath()
    {
        var validator = new ProposalValidator();
        var proposal = new ImprovementProposalDocument(
            ProposalKind.ParameterPatch,
            ImprovementRiskLevel.Low,
            "Unsafe",
            "Bad path",
            new[] { new EvidenceRef("observation", "obs-1") },
            "None",
            Array.Empty<string>(),
            new[]
            {
                new ParameterPatchSpec(
                    "Polymarket:Clob:PrivateKey",
                    "\"abc\"",
                    "Never allowed",
                    0.1m)
            },
            null);

        var result = validator.Validate(proposal);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("restricted", StringComparison.OrdinalIgnoreCase)
            || error.Contains("forbidden", StringComparison.OrdinalIgnoreCase));
    }
}
