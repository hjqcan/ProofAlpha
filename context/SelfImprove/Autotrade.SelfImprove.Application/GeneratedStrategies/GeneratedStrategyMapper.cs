using Autotrade.SelfImprove.Application.Contract.GeneratedStrategies;
using Autotrade.SelfImprove.Domain.Entities;

namespace Autotrade.SelfImprove.Application.GeneratedStrategies;

public static class GeneratedStrategyMapper
{
    public static GeneratedStrategyVersionDto ToDto(this GeneratedStrategyVersion version)
    {
        return new GeneratedStrategyVersionDto(
            version.Id,
            version.ProposalId,
            version.StrategyId,
            version.Version,
            version.Stage,
            version.ArtifactRoot,
            version.PackageHash,
            version.ManifestJson,
            version.RiskEnvelopeJson,
            version.ValidationSummaryJson,
            version.IsActiveCanary,
            version.CreatedAtUtc,
            version.UpdatedAtUtc,
            version.QuarantineReason);
    }

    public static PromotionGateResultDto ToDto(this PromotionGateResult result)
    {
        return new PromotionGateResultDto(
            result.Id,
            result.GeneratedStrategyVersionId,
            result.Stage,
            result.Passed,
            result.Message,
            result.EvidenceJson,
            result.CreatedAtUtc);
    }
}
