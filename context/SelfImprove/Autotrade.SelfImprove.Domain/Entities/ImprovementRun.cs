using Autotrade.SelfImprove.Domain.Shared.Enums;
using NetDevPack.Domain;

namespace Autotrade.SelfImprove.Domain.Entities;

public sealed class ImprovementRun : Entity, IAggregateRoot
{
    private ImprovementRun()
    {
        StrategyId = string.Empty;
        WindowStartUtc = DateTimeOffset.UtcNow;
        WindowEndUtc = DateTimeOffset.UtcNow;
        Status = ImprovementRunStatus.Created;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public ImprovementRun(
        string strategyId,
        string? marketId,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        string trigger,
        DateTimeOffset createdAtUtc)
    {
        StrategyId = string.IsNullOrWhiteSpace(strategyId)
            ? throw new ArgumentException("StrategyId cannot be empty.", nameof(strategyId))
            : strategyId.Trim();
        MarketId = string.IsNullOrWhiteSpace(marketId) ? null : marketId.Trim();
        WindowStartUtc = windowStartUtc;
        WindowEndUtc = windowEndUtc <= windowStartUtc
            ? throw new ArgumentException("WindowEndUtc must be after WindowStartUtc.", nameof(windowEndUtc))
            : windowEndUtc;
        Trigger = string.IsNullOrWhiteSpace(trigger) ? "manual" : trigger.Trim();
        Status = ImprovementRunStatus.Created;
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public string StrategyId { get; private set; }

    public string? MarketId { get; private set; }

    public DateTimeOffset WindowStartUtc { get; private set; }

    public DateTimeOffset WindowEndUtc { get; private set; }

    public string Trigger { get; private set; } = "manual";

    public ImprovementRunStatus Status { get; private set; }

    public Guid? EpisodeId { get; private set; }

    public int ProposalCount { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void AttachEpisode(Guid episodeId)
    {
        EpisodeId = episodeId == Guid.Empty ? throw new ArgumentException("EpisodeId cannot be empty.", nameof(episodeId)) : episodeId;
        TransitionTo(ImprovementRunStatus.EpisodeBuilt);
    }

    public void MarkAnalyzed(int proposalCount, bool requiresManualReview)
    {
        ProposalCount = proposalCount < 0 ? 0 : proposalCount;
        TransitionTo(requiresManualReview ? ImprovementRunStatus.ManualReview : ImprovementRunStatus.Analyzed);
    }

    public void MarkCompleted() => TransitionTo(ImprovementRunStatus.Completed);

    public void MarkFailed(string message)
    {
        ErrorMessage = string.IsNullOrWhiteSpace(message) ? "Unknown failure." : message.Trim();
        TransitionTo(ImprovementRunStatus.Failed);
    }

    private void TransitionTo(ImprovementRunStatus status)
    {
        Status = status;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
