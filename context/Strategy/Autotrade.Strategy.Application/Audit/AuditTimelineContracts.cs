namespace Autotrade.Strategy.Application.Audit;

public enum AuditTimelineItemType
{
    StrategyDecision = 0,
    OrderEvent = 1,
    RiskEvent = 2,
    CommandAudit = 3
}

public sealed record AuditTimelineQuery(
    string? StrategyId = null,
    string? MarketId = null,
    Guid? OrderId = null,
    string? ClientOrderId = null,
    Guid? RunSessionId = null,
    Guid? RiskEventId = null,
    string? CorrelationId = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    int Limit = 200);

public sealed record AuditTimeline(
    DateTimeOffset GeneratedAtUtc,
    int Count,
    int Limit,
    AuditTimelineQuery Query,
    IReadOnlyList<AuditTimelineItem> Items);

public sealed record AuditTimelineItem(
    Guid ItemId,
    DateTimeOffset TimestampUtc,
    AuditTimelineItemType Type,
    string Source,
    string Actor,
    string Summary,
    string DetailReference,
    string? StrategyId,
    string? MarketId,
    Guid? OrderId,
    string? ClientOrderId,
    Guid? RunSessionId,
    Guid? RiskEventId,
    string? CorrelationId,
    string? DetailJson);
