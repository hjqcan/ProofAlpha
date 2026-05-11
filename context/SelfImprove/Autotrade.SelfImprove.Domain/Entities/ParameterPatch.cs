using NetDevPack.Domain;

namespace Autotrade.SelfImprove.Domain.Entities;

public sealed class ParameterPatch : Entity, IAggregateRoot
{
    private ParameterPatch()
    {
        StrategyId = string.Empty;
        Path = string.Empty;
        NewValueJson = "null";
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public ParameterPatch(
        Guid proposalId,
        string strategyId,
        string path,
        string? oldValueJson,
        string newValueJson,
        DateTimeOffset createdAtUtc)
    {
        ProposalId = proposalId == Guid.Empty ? throw new ArgumentException("ProposalId cannot be empty.", nameof(proposalId)) : proposalId;
        StrategyId = string.IsNullOrWhiteSpace(strategyId)
            ? throw new ArgumentException("StrategyId cannot be empty.", nameof(strategyId))
            : strategyId.Trim();
        Path = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("Path cannot be empty.", nameof(path))
            : path.Trim();
        OldValueJson = string.IsNullOrWhiteSpace(oldValueJson) ? null : oldValueJson.Trim();
        NewValueJson = string.IsNullOrWhiteSpace(newValueJson) ? "null" : newValueJson.Trim();
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc;
    }

    public Guid ProposalId { get; private set; }

    public string StrategyId { get; private set; }

    public string Path { get; private set; }

    public string? OldValueJson { get; private set; }

    public string NewValueJson { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
