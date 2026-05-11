using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.OpportunityDiscovery.Application.Contract.Analysis;

public sealed record OpportunityAnalysisResponse(
    IReadOnlyList<OpportunityAnalysisDocument>? Opportunities,
    string? AbstainReason);

public sealed record OpportunityAnalysisDocument
{
    public string? MarketId { get; init; }

    public OutcomeSide Outcome { get; init; }

    public decimal FairProbability { get; init; }

    public decimal Confidence { get; init; }

    public decimal Edge { get; init; }

    public string Reason { get; init; } = string.Empty;

    public IReadOnlyList<Guid> EvidenceIds { get; init; } = Array.Empty<Guid>();

    public decimal EntryMaxPrice { get; init; }

    public decimal TakeProfitPrice { get; init; }

    public decimal StopLossPrice { get; init; }

    public decimal MaxSpread { get; init; } = 0.08m;

    public decimal Quantity { get; init; } = 1m;

    public decimal MaxNotional { get; init; } = 5m;

    public DateTimeOffset ValidUntilUtc { get; init; }

    public string? AbstainReason { get; init; }
}
