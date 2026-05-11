using Autotrade.SelfImprove.Domain.Shared.Enums;
using NetDevPack.Domain;

namespace Autotrade.SelfImprove.Domain.Entities;

public sealed class PromotionGateResult : Entity, IAggregateRoot
{
    private PromotionGateResult()
    {
        Stage = PromotionGateStage.StaticValidation;
        Message = string.Empty;
        EvidenceJson = "{}";
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public PromotionGateResult(
        Guid generatedStrategyVersionId,
        PromotionGateStage stage,
        bool passed,
        string message,
        string evidenceJson,
        DateTimeOffset createdAtUtc)
    {
        GeneratedStrategyVersionId = generatedStrategyVersionId == Guid.Empty
            ? throw new ArgumentException("GeneratedStrategyVersionId cannot be empty.", nameof(generatedStrategyVersionId))
            : generatedStrategyVersionId;
        Stage = stage;
        Passed = passed;
        Message = string.IsNullOrWhiteSpace(message) ? (passed ? "Passed" : "Failed") : message.Trim();
        EvidenceJson = string.IsNullOrWhiteSpace(evidenceJson) ? "{}" : evidenceJson.Trim();
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc;
    }

    public Guid GeneratedStrategyVersionId { get; private set; }

    public PromotionGateStage Stage { get; private set; }

    public bool Passed { get; private set; }

    public string Message { get; private set; }

    public string EvidenceJson { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
