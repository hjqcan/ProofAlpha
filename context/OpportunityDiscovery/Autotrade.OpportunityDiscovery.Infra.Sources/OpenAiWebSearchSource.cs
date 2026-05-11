using System.Net.Http.Json;
using System.Text.Json;
using Autotrade.OpportunityDiscovery.Application;
using Autotrade.OpportunityDiscovery.Application.Evidence;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using Microsoft.Extensions.Options;

namespace Autotrade.OpportunityDiscovery.Infra.Sources;

public sealed class OpenAiWebSearchSource : IEvidenceSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly OpportunityDiscoveryOptions _options;

    public OpenAiWebSearchSource(HttpClient httpClient, IOptions<OpportunityDiscoveryOptions> options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public string Name => "openai_web_search";

    public async Task<IReadOnlyList<NormalizedEvidence>> SearchAsync(
        EvidenceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!_options.OpenAiWebSearch.Enabled)
        {
            return Array.Empty<NormalizedEvidence>();
        }

        var apiKey = ResolveApiKey(_options.OpenAiWebSearch.ApiKeyEnvVar);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Array.Empty<NormalizedEvidence>();
        }

        var baseUrl = string.IsNullOrWhiteSpace(_options.OpenAiWebSearch.BaseUrl)
            ? "https://api.openai.com/v1"
            : _options.OpenAiWebSearch.BaseUrl.TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/responses");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new
        {
            model = _options.OpenAiWebSearch.Model,
            tools = new[] { new { type = "web_search" } },
            tool_choice = "auto",
            include = new[] { "web_search_call.action.sources" },
            input = $"Find recent reliable public information relevant to this Polymarket market. Market: {query.Market.Name}. Return concise evidence with source URLs."
        }, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return Array.Empty<NormalizedEvidence>();
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return Parse(json, query.MaxItems);
    }

    internal static IReadOnlyList<NormalizedEvidence> Parse(string json, int maxItems)
    {
        using var document = JsonDocument.Parse(json);
        var now = DateTimeOffset.UtcNow;
        var results = new List<NormalizedEvidence>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!document.RootElement.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var item in output.EnumerateArray())
        {
            AddSources(item, results, seenUrls, now, maxItems);
            if (results.Count >= maxItems)
            {
                return results;
            }

            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                var text = contentItem.TryGetProperty("text", out var textElement)
                    ? textElement.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var url = TryReadFirstCitationUrl(contentItem) ?? $"openai:web-search:{results.Count + 1}";
                AddResult(results, seenUrls, url, FirstLine(text), text.Trim(), now, contentItem.GetRawText(), 0.60m);

                if (results.Count >= maxItems)
                {
                    return results;
                }
            }
        }

        return results;
    }

    private static void AddSources(
        JsonElement outputItem,
        List<NormalizedEvidence> results,
        HashSet<string> seenUrls,
        DateTimeOffset now,
        int maxItems)
    {
        if (!outputItem.TryGetProperty("action", out var action) ||
            !action.TryGetProperty("sources", out var sources) ||
            sources.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var source in sources.EnumerateArray())
        {
            if (results.Count >= maxItems)
            {
                return;
            }

            if (!source.TryGetProperty("url", out var urlElement) ||
                urlElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var url = urlElement.GetString();
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            var title = source.TryGetProperty("title", out var titleElement)
                ? titleElement.GetString()
                : null;
            AddResult(
                results,
                seenUrls,
                url,
                string.IsNullOrWhiteSpace(title) ? url : title,
                string.IsNullOrWhiteSpace(title) ? url : title,
                now,
                source.GetRawText(),
                0.60m);
        }
    }

    private static void AddResult(
        List<NormalizedEvidence> results,
        HashSet<string> seenUrls,
        string url,
        string title,
        string summary,
        DateTimeOffset now,
        string rawJson,
        decimal sourceQuality)
    {
        if (!seenUrls.Add(url))
        {
            return;
        }

        results.Add(new NormalizedEvidence(
            EvidenceSourceKind.OpenAiWebSearch,
            "openai_web_search",
            url,
            title,
            summary,
            null,
            now,
            rawJson,
            sourceQuality));
    }

    private static string? TryReadFirstCitationUrl(JsonElement contentItem)
    {
        if (!contentItem.TryGetProperty("annotations", out var annotations) || annotations.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var annotation in annotations.EnumerateArray())
        {
            if (annotation.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String)
            {
                return url.GetString();
            }
        }

        return null;
    }

    private static string FirstLine(string text)
    {
        var first = text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.Trim();
        if (string.IsNullOrWhiteSpace(first))
        {
            return "OpenAI web search result";
        }

        return first.Length <= 512 ? first : first[..512];
    }

    private static string? ResolveApiKey(string envVarName)
    {
        if (string.IsNullOrWhiteSpace(envVarName))
        {
            return null;
        }

        return Environment.GetEnvironmentVariable(envVarName)?.Trim();
    }
}
