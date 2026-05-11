// ============================================================================
// 策略决策查询条件
// ============================================================================

namespace Autotrade.Strategy.Application.Decisions;

/// <summary>
/// 策略决策查询条件。
/// </summary>
/// <param name="StrategyId">策略 ID（可选）。</param>
/// <param name="MarketId">市场 ID（可选）。</param>
/// <param name="FromUtc">开始时间（可选）。</param>
/// <param name="ToUtc">结束时间（可选）。</param>
/// <param name="Limit">最大返回条数。</param>
public sealed record StrategyDecisionQuery(
    string? StrategyId,
    string? MarketId,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    int Limit = 200,
    string? Action = null,
    string? CorrelationId = null,
    Guid? RunSessionId = null);
