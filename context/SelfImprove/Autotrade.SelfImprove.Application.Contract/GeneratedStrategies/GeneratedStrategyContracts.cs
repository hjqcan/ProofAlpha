using Autotrade.SelfImprove.Domain.Shared.Enums;

namespace Autotrade.SelfImprove.Application.Contract.GeneratedStrategies;

public sealed record GeneratedStrategyManifest(
    string StrategyId,
    string Name,
    string Version,
    string EntryPoint,
    string PackageHash,
    string ParameterSchemaPath,
    string ReplaySpecPath,
    string RiskEnvelopePath,
    bool Enabled,
    string ConfigVersion);

public sealed record GeneratedStrategyVersionDto(
    Guid Id,
    Guid ProposalId,
    string StrategyId,
    string Version,
    GeneratedStrategyStage Stage,
    string ArtifactRoot,
    string PackageHash,
    string ManifestJson,
    string RiskEnvelopeJson,
    string? ValidationSummaryJson,
    bool IsActiveCanary,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? QuarantineReason);

public sealed record PromotionGateResultDto(
    Guid Id,
    Guid GeneratedStrategyVersionId,
    PromotionGateStage Stage,
    bool Passed,
    string Message,
    string EvidenceJson,
    DateTimeOffset CreatedAtUtc);

public sealed record GeneratedStrategyValidationResult(
    bool Passed,
    IReadOnlyList<string> Errors,
    string EvidenceJson,
    IReadOnlyList<GeneratedStrategyGateValidationResult> Gates)
{
    public GeneratedStrategyValidationResult(
        bool passed,
        IReadOnlyList<string> errors,
        string evidenceJson)
        : this(passed, errors, evidenceJson, Array.Empty<GeneratedStrategyGateValidationResult>())
    {
    }
}

public sealed record GeneratedStrategyGateValidationResult(
    PromotionGateStage Stage,
    bool Passed,
    IReadOnlyList<string> Errors,
    string EvidenceJson);
