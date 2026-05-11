// ============================================================================
// 策略决策查询服务
// ============================================================================
// 提供策略决策日志的查询功能，用于 CLI export 命令。
// ============================================================================

using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Domain.Entities;

namespace Autotrade.Strategy.Application.Decisions;

/// <summary>
/// 策略决策查询服务接口。
/// </summary>
public interface IStrategyDecisionQueryService
{
    /// <summary>
    /// 查询策略决策日志。
    /// </summary>
    Task<IReadOnlyList<StrategyDecision>> QueryAsync(StrategyDecisionQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StrategyDecisionRecord>> QueryRecordsAsync(
        StrategyDecisionQuery query,
        CancellationToken cancellationToken = default);

    Task<StrategyDecisionRecord?> GetAsync(Guid decisionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// 策略决策查询服务。
/// </summary>
public sealed class StrategyDecisionQueryService : IStrategyDecisionQueryService
{
    private readonly IStrategyDecisionRepository _repository;

    public StrategyDecisionQueryService(IStrategyDecisionRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>
    /// 查询策略决策日志。
    /// </summary>
    public async Task<IReadOnlyList<StrategyDecision>> QueryAsync(
        StrategyDecisionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var items = await _repository.QueryAsync(query, cancellationToken).ConfigureAwait(false);
        return items.Select(ToDecision).ToList();
    }

    public async Task<IReadOnlyList<StrategyDecisionRecord>> QueryRecordsAsync(
        StrategyDecisionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var items = await _repository.QueryAsync(query, cancellationToken).ConfigureAwait(false);
        return items.Select(ToRecord).ToList();
    }

    public async Task<StrategyDecisionRecord?> GetAsync(
        Guid decisionId,
        CancellationToken cancellationToken = default)
    {
        if (decisionId == Guid.Empty)
        {
            return null;
        }

        var item = await _repository.GetAsync(decisionId, cancellationToken).ConfigureAwait(false);
        return item is null ? null : ToRecord(item);
    }

    private static StrategyDecision ToDecision(StrategyDecisionLog log)
    {
        return new StrategyDecision(
            log.StrategyId,
            log.Action,
            log.Reason,
            log.MarketId,
            log.ContextJson,
            log.CreatedAtUtc,
            log.CorrelationId,
            log.ExecutionMode,
            log.RunSessionId);
    }

    private static StrategyDecisionRecord ToRecord(StrategyDecisionLog log)
    {
        return new StrategyDecisionRecord(
            log.Id,
            log.StrategyId,
            log.Action,
            log.Reason,
            log.MarketId,
            log.ContextJson,
            log.CreatedAtUtc,
            log.ConfigVersion,
            log.CorrelationId,
            log.ExecutionMode,
            log.RunSessionId);
    }
}
