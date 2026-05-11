using Autotrade.SelfImprove.Domain.Shared.Enums;
using NetDevPack.Domain;

namespace Autotrade.SelfImprove.Domain.Entities;

public sealed class ImprovementProposal : Entity, IAggregateRoot
{
    private ImprovementProposal()
    {
        StrategyId = string.Empty;
        Title = string.Empty;
        Rationale = string.Empty;
        EvidenceJson = "[]";
        ExpectedImpactJson = "{}";
        RollbackConditionsJson = "[]";
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public ImprovementProposal(
        Guid runId,
        string strategyId,
        ProposalKind kind,
        ImprovementRiskLevel riskLevel,
        string title,
        string rationale,
        string evidenceJson,
        string expectedImpactJson,
        string rollbackConditionsJson,
        string? parameterPatchJson,
        string? codeGenerationSpecJson,
        bool requiresManualReview,
        DateTimeOffset createdAtUtc)
    {
        RunId = runId == Guid.Empty ? throw new ArgumentException("RunId cannot be empty.", nameof(runId)) : runId;
        StrategyId = string.IsNullOrWhiteSpace(strategyId)
            ? throw new ArgumentException("StrategyId cannot be empty.", nameof(strategyId))
            : strategyId.Trim();
        Kind = kind;
        RiskLevel = riskLevel;
        Title = string.IsNullOrWhiteSpace(title) ? "Untitled proposal" : title.Trim();
        Rationale = string.IsNullOrWhiteSpace(rationale) ? "No rationale supplied." : rationale.Trim();
        EvidenceJson = string.IsNullOrWhiteSpace(evidenceJson) ? "[]" : evidenceJson.Trim();
        ExpectedImpactJson = string.IsNullOrWhiteSpace(expectedImpactJson) ? "{}" : expectedImpactJson.Trim();
        RollbackConditionsJson = string.IsNullOrWhiteSpace(rollbackConditionsJson) ? "[]" : rollbackConditionsJson.Trim();
        ParameterPatchJson = string.IsNullOrWhiteSpace(parameterPatchJson) ? null : parameterPatchJson.Trim();
        CodeGenerationSpecJson = string.IsNullOrWhiteSpace(codeGenerationSpecJson) ? null : codeGenerationSpecJson.Trim();
        RequiresManualReview = requiresManualReview;
        Status = requiresManualReview ? ImprovementProposalStatus.ManualReview : ImprovementProposalStatus.Proposed;
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc;
    }

    public Guid RunId { get; private set; }

    public string StrategyId { get; private set; }

    public ProposalKind Kind { get; private set; }

    public ImprovementProposalStatus Status { get; private set; }

    public ImprovementRiskLevel RiskLevel { get; private set; }

    public string Title { get; private set; }

    public string Rationale { get; private set; }

    public string EvidenceJson { get; private set; }

    public string ExpectedImpactJson { get; private set; }

    public string RollbackConditionsJson { get; private set; }

    public string? ParameterPatchJson { get; private set; }

    public string? CodeGenerationSpecJson { get; private set; }

    public bool RequiresManualReview { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? AppliedAtUtc { get; private set; }

    public void Approve()
    {
        if (Status != ImprovementProposalStatus.Proposed && Status != ImprovementProposalStatus.ManualReview)
        {
            throw new InvalidOperationException($"Proposal cannot be approved from {Status}.");
        }

        Status = ImprovementProposalStatus.Approved;
    }

    public void MarkApplied()
    {
        Status = ImprovementProposalStatus.Applied;
        AppliedAtUtc = DateTimeOffset.UtcNow;
    }

    public void MarkRolledBack() => Status = ImprovementProposalStatus.RolledBack;

    public void Reject() => Status = ImprovementProposalStatus.Rejected;
}
