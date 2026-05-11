using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using NetDevPack.Domain;

namespace Autotrade.OpportunityDiscovery.Domain.Entities;

public sealed class OpportunityReview : Entity, IAggregateRoot
{
    private OpportunityReview()
    {
        Actor = string.Empty;
        Notes = string.Empty;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public OpportunityReview(
        Guid opportunityId,
        OpportunityReviewDecision decision,
        string actor,
        string? notes,
        DateTimeOffset createdAtUtc)
    {
        OpportunityId = opportunityId == Guid.Empty
            ? throw new ArgumentException("OpportunityId cannot be empty.", nameof(opportunityId))
            : opportunityId;
        Decision = decision;
        Actor = string.IsNullOrWhiteSpace(actor) ? "unknown" : actor.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? string.Empty : notes.Trim();
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc;
    }

    public Guid OpportunityId { get; private set; }

    public OpportunityReviewDecision Decision { get; private set; }

    public string Actor { get; private set; }

    public string Notes { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
