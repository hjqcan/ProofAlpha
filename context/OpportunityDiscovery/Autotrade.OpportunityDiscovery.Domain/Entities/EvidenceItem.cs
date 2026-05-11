using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using NetDevPack.Domain;

namespace Autotrade.OpportunityDiscovery.Domain.Entities;

public sealed class EvidenceItem : Entity, IAggregateRoot
{
    private EvidenceItem()
    {
        SourceName = string.Empty;
        Url = string.Empty;
        Title = string.Empty;
        Summary = string.Empty;
        ContentHash = string.Empty;
        RawJson = "{}";
        CreatedAtUtc = DateTimeOffset.UtcNow;
        ObservedAtUtc = DateTimeOffset.UtcNow;
    }

    public EvidenceItem(
        Guid researchRunId,
        EvidenceSourceKind sourceKind,
        string sourceName,
        string url,
        string title,
        string summary,
        DateTimeOffset? publishedAtUtc,
        DateTimeOffset observedAtUtc,
        string contentHash,
        string rawJson,
        decimal sourceQuality)
    {
        ResearchRunId = researchRunId == Guid.Empty
            ? throw new ArgumentException("ResearchRunId cannot be empty.", nameof(researchRunId))
            : researchRunId;
        SourceKind = sourceKind;
        SourceName = Required(sourceName, nameof(sourceName), 128);
        Url = Required(url, nameof(url), 2048);
        Title = Required(title, nameof(title), 512);
        Summary = string.IsNullOrWhiteSpace(summary) ? string.Empty : summary.Trim();
        PublishedAtUtc = publishedAtUtc;
        ObservedAtUtc = observedAtUtc == default ? DateTimeOffset.UtcNow : observedAtUtc;
        ContentHash = Required(contentHash, nameof(contentHash), 128);
        RawJson = string.IsNullOrWhiteSpace(rawJson) ? "{}" : rawJson.Trim();
        SourceQuality = Math.Clamp(sourceQuality, 0m, 1m);
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid ResearchRunId { get; private set; }

    public EvidenceSourceKind SourceKind { get; private set; }

    public string SourceName { get; private set; }

    public string Url { get; private set; }

    public string Title { get; private set; }

    public string Summary { get; private set; }

    public DateTimeOffset? PublishedAtUtc { get; private set; }

    public DateTimeOffset ObservedAtUtc { get; private set; }

    public string ContentHash { get; private set; }

    public string RawJson { get; private set; }

    public decimal SourceQuality { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    private static string Required(string value, string paramName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{paramName} cannot be empty.", paramName);
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
