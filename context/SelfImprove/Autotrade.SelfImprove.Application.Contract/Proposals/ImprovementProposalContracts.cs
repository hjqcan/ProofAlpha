using Autotrade.SelfImprove.Domain.Shared.Enums;

namespace Autotrade.SelfImprove.Application.Contract.Proposals;

public sealed record EvidenceRef(
    string Source,
    string Id,
    string? Reason = null);

public sealed record ParameterPatchSpec(
    string Path,
    string ValueJson,
    string Reason,
    decimal? MaxRelativeChange = null);

public sealed record GeneratedStrategySpec(
    string StrategyId,
    string Name,
    string Description,
    string PythonModule,
    string ManifestJson,
    string ParameterSchemaJson,
    string UnitTests,
    string ReplaySpecJson,
    string RiskEnvelopeJson);

public sealed record ImprovementProposalDocument(
    ProposalKind Kind,
    ImprovementRiskLevel RiskLevel,
    string Title,
    string Rationale,
    IReadOnlyList<EvidenceRef> Evidence,
    string ExpectedImpact,
    IReadOnlyList<string> RollbackConditions,
    IReadOnlyList<ParameterPatchSpec> ParameterPatches,
    GeneratedStrategySpec? GeneratedStrategy);

public sealed record ImprovementProposalDto(
    Guid Id,
    Guid RunId,
    string StrategyId,
    ProposalKind Kind,
    ImprovementProposalStatus Status,
    ImprovementRiskLevel RiskLevel,
    string Title,
    string Rationale,
    string EvidenceJson,
    string ExpectedImpactJson,
    string RollbackConditionsJson,
    string? ParameterPatchJson,
    string? CodeGenerationSpecJson,
    bool RequiresManualReview,
    DateTimeOffset CreatedAtUtc);

public sealed record ProposalValidationResult(
    bool IsValid,
    bool RequiresManualReview,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
