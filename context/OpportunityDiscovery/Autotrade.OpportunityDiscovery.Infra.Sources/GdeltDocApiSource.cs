using System.Text.Json;
using Autotrade.OpportunityDiscovery.Application;
using Autotrade.OpportunityDiscovery.Application.Evidence;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using Microsoft.Extensions.Options;

namespace Autotrade.OpportunityDiscovery.Infra.Sources;

public sealed class GdeltDocApiSource : IEvidenceSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly OpportunityDiscoveryOptions _options;

    public GdeltDocApiSource(HttpClient httpClient, IOptions<OpportunityDiscoveryOptions> options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public string Name => "gdelt";

    public async Task<IReadOnlyList<NormalizedEvidence>> SearchAsync(
        EvidenceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!_options.Gdelt.Enabled)
        {
            return Array.Empty<NormalizedEvidence>();
        }

        var baseUrl = string.IsNullOrWhiteSpace(_options.Gdelt.BaseUrl)
            ? "https://api.gdeltproject.org/api/v2/doc/doc"
            : _options.Gdelt.BaseUrl;
        var maxRecords = Math.Clamp(Math.Min(query.MaxItems, _options.Gdelt.MaxRecords), 1, 250);
        var url = $"{baseUrl}?query={Uri.EscapeDataString(query.Market.Name)}&mode=artlist&format=json&maxrecords={maxRecords}&sort=hybridrel";
        var json = await _httpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        return Parse(json, query.MaxItems);
    }

    internal static IReadOnlyList<NormalizedEvidence> Parse(string json, int maxItems)
    {
        var response = JsonSerializer.Deserialize<GdeltResponse>(json, JsonOptions);
        var now = DateTimeOffset.UtcNow;
        return (response?.Articles ?? Array.Empty<GdeltArticle>())
            .Where(article => !string.IsNullOrWhiteSpace(article.Url))
            .Take(maxItems)
            .Select(article => new NormalizedEvidence(
                EvidenceSourceKind.Gdelt,
                string.IsNullOrWhiteSpace(article.Domain) ? "gdelt" : article.Domain!,
                article.Url!,
                article.Title ?? article.Url!,
                article.Seendate ?? string.Empty,
                ParseSeenDate(article.Seendate),
                now,
                JsonSerializer.Serialize(article, JsonOptions),
                0.70m))
            .ToList();
    }

    private static DateTimeOffset? ParseSeenDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length == 14 &&
            DateTimeOffset.TryParseExact(
                normalized,
                "yyyyMMddHHmmss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return DateTimeOffset.TryParse(normalized, out var fallback)
            ? fallback.ToUniversalTime()
            : null;
    }

    private sealed record GdeltResponse(IReadOnlyList<GdeltArticle>? Articles);

    private sealed record GdeltArticle(
        string? Url,
        string? Title,
        string? Domain,
        string? Seendate);
}
