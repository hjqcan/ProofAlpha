namespace Autotrade.Strategy.Application.Promotion;

public sealed class PaperPromotionChecklistOptions
{
    public const string SectionName = "PaperPromotion";

    public int MinRunDurationMinutes { get; set; } = 30;

    public int MaxOrderErrorCount { get; set; } = 0;

    public int MaxUnhedgedExposures { get; set; } = 0;

    public int MaxAccountSyncAgeSeconds { get; set; } = 300;

    public bool RequireStoppedSession { get; set; } = true;

    public bool RequireTradesForPnlAttribution { get; set; } = true;
}

public interface IPaperPromotionChecklistService
{
    Task<PaperPromotionChecklist?> EvaluateAsync(
        Guid sessionId,
        int limit = 1000,
        CancellationToken cancellationToken = default);
}

public sealed record PaperPromotionChecklist(
    Guid SessionId,
    DateTimeOffset GeneratedAtUtc,
    string OverallStatus,
    bool CanConsiderLive,
    bool LiveArmingUnchanged,
    IReadOnlyList<PaperPromotionCriterion> Criteria,
    IReadOnlyList<string> ResidualRisks);

public sealed record PaperPromotionCriterion(
    string Id,
    string Name,
    string Status,
    string Reason,
    IReadOnlyList<Guid> EvidenceIds,
    IReadOnlyList<string> ResidualRisks);
