using Autotrade.Application.Services;

namespace Autotrade.ArcSettlement.Application.Contract.Provenance;

public enum ArcProvenanceSourceModule
{
    OpportunityDiscovery = 0,
    SelfImprove = 1,
    ManualFixture = 2
}

public enum ArcOpportunityProvenanceStatus
{
    Candidate = 0,
    NeedsReview = 1,
    Approved = 2,
    Published = 3,
    Rejected = 4,
    Expired = 5
}

public enum ArcGeneratedPackageValidationStage
{
    Generated = 0,
    StaticValidated = 1,
    UnitTested = 2,
    ReplayValidated = 3,
    ShadowRunning = 4,
    PaperRunning = 5,
    LiveCanary = 6,
    Promoted = 7,
    RolledBack = 8,
    Quarantined = 9
}

public enum ArcProvenanceValidationStatus
{
    Approved = 0,
    Published = 1,
    StaticValidated = 2,
    UnitTested = 3,
    ReplayValidated = 4,
    ShadowRunning = 5,
    PaperRunning = 6,
    LiveCanary = 7,
    Promoted = 8
}

public sealed record ArcProvenanceEvidenceReference(
    string EvidenceId,
    string Title,
    string Summary,
    string ContentHash,
    string? SourceUri = null,
    DateTimeOffset? ObservedAtUtc = null);

public sealed record ArcStrategyProvenanceDocument(
    string DocumentVersion,
    ArcProvenanceSourceModule SourceModule,
    string SourceId,
    string AgentId,
    string MarketId,
    string StrategyId,
    IReadOnlyList<string> EvidenceIds,
    string EvidenceSummaryHash,
    string LlmOutputHash,
    string CompiledPolicyHash,
    string? GeneratedPackageHash,
    string RiskEnvelopeHash,
    ArcProvenanceValidationStatus ValidationStatus,
    DateTimeOffset CreatedAtUtc);

public sealed record ArcStrategyProvenanceRecord(
    string ProvenanceHash,
    ArcStrategyProvenanceDocument Document,
    IReadOnlyList<ArcProvenanceEvidenceReference> Evidence,
    string? EvidenceUri,
    string PrivacyNote,
    DateTimeOffset RecordedAtUtc);

public sealed record BuildOpportunityProvenanceRequest(
    string SourceId,
    string AgentId,
    string MarketId,
    string StrategyId,
    ArcOpportunityProvenanceStatus Status,
    IReadOnlyList<ArcProvenanceEvidenceReference> Evidence,
    string LlmOutputJson,
    string CompiledPolicyJson,
    string RiskEnvelopeJson,
    DateTimeOffset? CreatedAtUtc = null,
    string? EvidenceUri = null);

public sealed record BuildGeneratedPackageProvenanceRequest(
    string SourceId,
    string AgentId,
    string MarketId,
    string StrategyId,
    string PackageHash,
    string ManifestJson,
    string RiskEnvelopeJson,
    string ValidationSummaryJson,
    ArcGeneratedPackageValidationStage ValidationStage,
    IReadOnlyList<ArcProvenanceEvidenceReference> Evidence,
    string LlmOutputJson,
    DateTimeOffset? CreatedAtUtc = null,
    string? EvidenceUri = null);

public sealed record ArcSubscriberProvenanceExplanation(
    string ProvenanceHash,
    ArcProvenanceSourceModule SourceModule,
    string SourceId,
    string AgentId,
    string MarketId,
    string StrategyId,
    ArcProvenanceValidationStatus ValidationStatus,
    IReadOnlyList<ArcProvenanceEvidenceReference> Evidence,
    string EvidenceSummaryHash,
    string LlmOutputHash,
    string CompiledPolicyHash,
    string? GeneratedPackageHash,
    string RiskEnvelopeHash,
    string? EvidenceUri,
    string PrivacyNote,
    DateTimeOffset CreatedAtUtc);

public sealed class ArcProvenanceRejectedException(
    string errorCode,
    string message) : InvalidOperationException(message)
{
    public string ErrorCode { get; } = errorCode;
}

public interface IArcStrategyProvenanceService : IApplicationService
{
    Task<ArcStrategyProvenanceRecord> ExportOpportunityAsync(
        BuildOpportunityProvenanceRequest request,
        CancellationToken cancellationToken = default);

    Task<ArcStrategyProvenanceRecord> AnchorGeneratedPackageAsync(
        BuildGeneratedPackageProvenanceRequest request,
        CancellationToken cancellationToken = default);

    Task<ArcStrategyProvenanceRecord?> GetAsync(
        string provenanceHash,
        CancellationToken cancellationToken = default);

    Task<ArcSubscriberProvenanceExplanation?> GetSubscriberExplanationAsync(
        string provenanceHash,
        CancellationToken cancellationToken = default);
}
