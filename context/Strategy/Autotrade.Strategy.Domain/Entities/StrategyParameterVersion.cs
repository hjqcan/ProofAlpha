using NetDevPack.Domain;

namespace Autotrade.Strategy.Domain.Entities;

public sealed class StrategyParameterVersion : Entity, IAggregateRoot
{
    private StrategyParameterVersion()
    {
        StrategyId = string.Empty;
        ConfigVersion = string.Empty;
        SnapshotJson = "{}";
        DiffJson = "[]";
        ChangeType = string.Empty;
        Source = string.Empty;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public StrategyParameterVersion(
        string strategyId,
        string configVersion,
        string? previousConfigVersion,
        string snapshotJson,
        string diffJson,
        string changeType,
        string source,
        string? actor,
        string? reason,
        DateTimeOffset createdAtUtc,
        Guid? rollbackSourceVersionId = null)
    {
        StrategyId = string.IsNullOrWhiteSpace(strategyId)
            ? throw new ArgumentException("StrategyId cannot be empty.", nameof(strategyId))
            : strategyId.Trim();

        ConfigVersion = string.IsNullOrWhiteSpace(configVersion)
            ? throw new ArgumentException("ConfigVersion cannot be empty.", nameof(configVersion))
            : configVersion.Trim();

        PreviousConfigVersion = string.IsNullOrWhiteSpace(previousConfigVersion)
            ? null
            : previousConfigVersion.Trim();

        SnapshotJson = string.IsNullOrWhiteSpace(snapshotJson) ? "{}" : snapshotJson.Trim();
        DiffJson = string.IsNullOrWhiteSpace(diffJson) ? "[]" : diffJson.Trim();

        ChangeType = string.IsNullOrWhiteSpace(changeType)
            ? throw new ArgumentException("ChangeType cannot be empty.", nameof(changeType))
            : changeType.Trim();

        Source = string.IsNullOrWhiteSpace(source)
            ? throw new ArgumentException("Source cannot be empty.", nameof(source))
            : source.Trim();

        Actor = string.IsNullOrWhiteSpace(actor) ? null : actor.Trim();
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc;
        RollbackSourceVersionId = rollbackSourceVersionId;
    }

    public string StrategyId { get; private set; }

    public string ConfigVersion { get; private set; }

    public string? PreviousConfigVersion { get; private set; }

    public string SnapshotJson { get; private set; }

    public string DiffJson { get; private set; }

    public string ChangeType { get; private set; }

    public string Source { get; private set; }

    public string? Actor { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid? RollbackSourceVersionId { get; private set; }
}
