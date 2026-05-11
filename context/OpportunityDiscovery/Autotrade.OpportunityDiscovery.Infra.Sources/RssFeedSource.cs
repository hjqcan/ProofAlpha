using System.Text.Json;
using System.Xml.Linq;
using Autotrade.OpportunityDiscovery.Application;
using Autotrade.OpportunityDiscovery.Application.Evidence;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using Microsoft.Extensions.Options;

namespace Autotrade.OpportunityDiscovery.Infra.Sources;

public sealed class RssFeedSource : IEvidenceSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly OpportunityDiscoveryOptions _options;

    public RssFeedSource(HttpClient httpClient, IOptions<OpportunityDiscoveryOptions> options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public string Name => "rss";

    public async Task<IReadOnlyList<NormalizedEvidence>> SearchAsync(
        EvidenceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!_options.Rss.Enabled || _options.Rss.FeedUrls.Count == 0)
        {
            return Array.Empty<NormalizedEvidence>();
        }

        var terms = BuildTerms(query.Market.Name);
        var results = new List<NormalizedEvidence>();
        foreach (var feedUrl in _options.Rss.FeedUrls.Where(url => !string.IsNullOrWhiteSpace(url)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var xml = await _httpClient.GetStringAsync(feedUrl, cancellationToken).ConfigureAwait(false);
            results.AddRange(ParseFeed(feedUrl, xml, terms, Math.Max(1, _options.Rss.MaxItemsPerFeed)));
        }

        return results
            .OrderByDescending(item => item.PublishedAtUtc ?? item.ObservedAtUtc)
            .Take(query.MaxItems)
            .ToList();
    }

    internal static IReadOnlyList<NormalizedEvidence> ParseFeed(
        string feedUrl,
        string xml,
        IReadOnlyList<string> terms,
        int maxItems)
    {
        var document = XDocument.Parse(xml);
        var now = DateTimeOffset.UtcNow;
        var items = document.Descendants()
            .Where(element => element.Name.LocalName is "item" or "entry")
            .Take(maxItems * 3);

        var results = new List<NormalizedEvidence>();
        foreach (var item in items)
        {
            var title = ReadChild(item, "title");
            var summary = ReadChild(item, "description") ?? ReadChild(item, "summary") ?? ReadChild(item, "content") ?? string.Empty;
            var text = $"{title} {summary}";
            if (!Matches(text, terms))
            {
                continue;
            }

            var url = ReadChild(item, "link") ?? ReadLinkHref(item) ?? feedUrl;
            var published = TryParseDate(ReadChild(item, "pubDate") ?? ReadChild(item, "published") ?? ReadChild(item, "updated"));
            results.Add(new NormalizedEvidence(
                EvidenceSourceKind.Rss,
                "rss",
                url,
                string.IsNullOrWhiteSpace(title) ? url : title,
                Strip(summary),
                published,
                now,
                JsonSerializer.Serialize(new { feedUrl, title, url, published }, JsonOptions),
                0.65m));
        }

        return results.Take(maxItems).ToList();
    }

    private static IReadOnlyList<string> BuildTerms(string marketName)
    {
        return marketName
            .Split([' ', '?', ',', '.', ':', ';', '/', '\\', '-', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries)
            .Where(term => term.Length >= 4)
            .Select(term => term.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static bool Matches(string text, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
        {
            return true;
        }

        var lower = text.ToLowerInvariant();
        return terms.Any(lower.Contains);
    }

    private static string? ReadChild(XElement item, string localName)
    {
        return item.Elements()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?.Trim();
    }

    private static string? ReadLinkHref(XElement item)
    {
        return item.Elements()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "link", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("href")
            ?.Value
            ?.Trim();
    }

    private static DateTimeOffset? TryParseDate(string? value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed.ToUniversalTime() : null;
    }

    private static string Strip(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("<![CDATA[", string.Empty, StringComparison.Ordinal)
                .Replace("]]>", string.Empty, StringComparison.Ordinal)
                .Trim();
    }
}
