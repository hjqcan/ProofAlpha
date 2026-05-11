using Autotrade.Trading.Domain.Shared.Enums;
using NetDevPack.Domain;

namespace Autotrade.Trading.Domain.Entities;

/// <summary>
/// 风控事件（可持久化，用于审计与复盘）。
/// </summary>
public sealed class RiskEvent : Entity
{
    // EF Core
    private RiskEvent()
    {
        TradingAccountId = Guid.Empty;
        Code = string.Empty;
        Message = string.Empty;
        Severity = RiskSeverity.Info;
        ContextJson = "{}";
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public RiskEvent(
        Guid tradingAccountId,
        string code,
        RiskSeverity severity,
        string message,
        string? contextJson = null)
    {
        if (tradingAccountId == Guid.Empty)
        {
            throw new ArgumentException("交易账户 ID 不能为空", nameof(tradingAccountId));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("风控码不能为空", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("风控消息不能为空", nameof(message));
        }

        TradingAccountId = tradingAccountId;
        Code = code.Trim();
        Severity = severity;
        Message = message.Trim();
        ContextJson = string.IsNullOrWhiteSpace(contextJson) ? "{}" : contextJson.Trim();
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid TradingAccountId { get; private set; }

    /// <summary>
    /// 风控原因码（可用于聚合统计与告警规则匹配）。
    /// </summary>
    public string Code { get; private set; }

    public RiskSeverity Severity { get; private set; }

    public string Message { get; private set; }

    /// <summary>
    /// 上下文信息（建议 JSON，便于落库与检索）。
    /// </summary>
    public string ContextJson { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}

