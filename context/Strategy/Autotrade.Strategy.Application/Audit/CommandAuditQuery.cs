namespace Autotrade.Strategy.Application.Audit;

public sealed record CommandAuditQuery(
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    int Limit = 200,
    string? CommandName = null,
    string? Actor = null);
