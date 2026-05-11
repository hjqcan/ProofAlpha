namespace Autotrade.Strategy.Application.Parameters;

public sealed record StrategyParameterValue(
    string Name,
    string Value,
    string Type,
    bool Editable);

public sealed record StrategyParameterDiff(
    string Name,
    string PreviousValue,
    string NextValue);

public sealed record StrategyParameterVersionRecord(
    Guid VersionId,
    string StrategyId,
    string ConfigVersion,
    string? PreviousConfigVersion,
    string ChangeType,
    string Source,
    string? Actor,
    string? Reason,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<StrategyParameterDiff> Diff,
    Guid? RollbackSourceVersionId);

public sealed record StrategyParameterSnapshot(
    string StrategyId,
    string ConfigVersion,
    IReadOnlyList<StrategyParameterValue> Parameters,
    IReadOnlyList<StrategyParameterVersionRecord> RecentVersions);

public sealed record StrategyParameterMutationRequest(
    IReadOnlyDictionary<string, string> Changes,
    string? Actor,
    string Source,
    string? Reason);

public sealed record StrategyParameterRollbackRequest(
    Guid VersionId,
    string? Actor,
    string Source,
    string? Reason);

public sealed record StrategyParameterMutationResult(
    bool Accepted,
    string Status,
    string Message,
    StrategyParameterVersionRecord? Version,
    StrategyParameterSnapshot Snapshot);

public interface IStrategyParameterVersionService
{
    Task<StrategyParameterSnapshot> GetSnapshotAsync(
        string strategyId,
        int limit = 10,
        CancellationToken cancellationToken = default);

    Task<StrategyParameterMutationResult> UpdateAsync(
        string strategyId,
        StrategyParameterMutationRequest request,
        CancellationToken cancellationToken = default);

    Task<StrategyParameterMutationResult> RollbackAsync(
        string strategyId,
        StrategyParameterRollbackRequest request,
        CancellationToken cancellationToken = default);

    Task ApplyLatestAcceptedVersionsAsync(CancellationToken cancellationToken = default);
}
