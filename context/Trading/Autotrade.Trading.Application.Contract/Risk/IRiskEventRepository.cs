using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Trading.Application.Contract.Risk;

/// <summary>
/// Risk event repository interface.
/// </summary>
public interface IRiskEventRepository
{
    /// <summary>
    /// Records a risk event.
    /// </summary>
    Task AddAsync(
        string code,
        RiskSeverity severity,
        string message,
        string? strategyId = null,
        string? contextJson = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries risk events.
    /// </summary>
    Task<IReadOnlyList<RiskEventRecord>> QueryAsync(
        string? strategyId = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<RiskEventRecord?> GetAsync(Guid riskEventId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Risk event record.
/// </summary>
public sealed record RiskEventRecord(
    Guid Id,
    string Code,
    RiskSeverity Severity,
    string Message,
    string? StrategyId,
    string? ContextJson,
    DateTimeOffset CreatedAtUtc,
    string? MarketId = null);
