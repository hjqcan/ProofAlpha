using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;

namespace Autotrade.OpportunityDiscovery.Application.Evidence;

public sealed record EvidenceQuery(
    Guid ResearchRunId,
    MarketInfoDto Market,
    int MaxItems);

public sealed record NormalizedEvidence(
    EvidenceSourceKind SourceKind,
    string SourceName,
    string Url,
    string Title,
    string Summary,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset ObservedAtUtc,
    string RawJson,
    decimal SourceQuality);

public interface IEvidenceSource
{
    string Name { get; }

    Task<IReadOnlyList<NormalizedEvidence>> SearchAsync(
        EvidenceQuery query,
        CancellationToken cancellationToken = default);
}
