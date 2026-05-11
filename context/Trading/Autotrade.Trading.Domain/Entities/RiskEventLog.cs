using Autotrade.Trading.Domain.Shared.Enums;
using NetDevPack.Domain;

namespace Autotrade.Trading.Domain.Entities;

/// <summary>
/// 独立的风控事件日志（不依赖 TradingAccount，用于审计与复盘）。
/// </summary>
public sealed class RiskEventLog : Entity
{
    // EF Core
    private RiskEventLog()
    {
        Code = string.Empty;
        Message = string.Empty;
        Severity = RiskSeverity.Info;
        ContextJson = "{}";
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public RiskEventLog(
        string code,
        RiskSeverity severity,
        string message,
        string? strategyId = null,
        string? marketId = null,
        string? contextJson = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("风控码不能为空", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("风控消息不能为空", nameof(message));
        }

        Code = code.Trim();
        Severity = severity;
        Message = message.Trim();
        StrategyId = strategyId?.Trim();
        MarketId = marketId?.Trim();
        ContextJson = string.IsNullOrWhiteSpace(contextJson) ? "{}" : contextJson.Trim();
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 风控原因码（可用于聚合统计与告警规则匹配）。
    /// </summary>
    public string Code { get; private set; }

    public RiskSeverity Severity { get; private set; }

    public string Message { get; private set; }

    /// <summary>
    /// 关联策略 ID（可选）。
    /// </summary>
    public string? StrategyId { get; private set; }

    /// <summary>
    /// 关联市场 ID（可选）。
    /// </summary>
    public string? MarketId { get; private set; }

    /// <summary>
    /// 上下文信息（JSON 格式，便于落库与检索）。
    /// </summary>
    public string ContextJson { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
