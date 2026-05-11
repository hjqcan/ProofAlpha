using Autotrade.SelfImprove.Domain.Shared.Enums;
using NetDevPack.Domain;

namespace Autotrade.SelfImprove.Domain.Entities;

public sealed class PatchOutcome : Entity, IAggregateRoot
{
    private PatchOutcome()
    {
        StrategyId = string.Empty;
        DiffJson = "{}";
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public PatchOutcome(
        Guid proposalId,
        string strategyId,
        PatchOutcomeStatus status,
        string diffJson,
        string? rollbackJson,
        string? message,
        DateTimeOffset createdAtUtc)
    {
        ProposalId = proposalId == Guid.Empty ? throw new ArgumentException("ProposalId cannot be empty.", nameof(proposalId)) : proposalId;
        StrategyId = string.IsNullOrWhiteSpace(strategyId)
            ? throw new ArgumentException("StrategyId cannot be empty.", nameof(strategyId))
            : strategyId.Trim();
        Status = status;
        DiffJson = string.IsNullOrWhiteSpace(diffJson) ? "{}" : diffJson.Trim();
        RollbackJson = string.IsNullOrWhiteSpace(rollbackJson) ? null : rollbackJson.Trim();
        Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc;
    }

    public Guid ProposalId { get; private set; }

    public string StrategyId { get; private set; }

    public PatchOutcomeStatus Status { get; private set; }

    public string DiffJson { get; private set; }

    public string? RollbackJson { get; private set; }

    public string? Message { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
