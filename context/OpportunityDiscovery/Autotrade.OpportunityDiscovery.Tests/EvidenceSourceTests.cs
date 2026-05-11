using System.Net;
using System.Text;
using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.OpportunityDiscovery.Application;
using Autotrade.OpportunityDiscovery.Application.Evidence;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using Autotrade.OpportunityDiscovery.Infra.Sources;
using Microsoft.Extensions.Options;

namespace Autotrade.OpportunityDiscovery.Tests;

public sealed class EvidenceSourceTests
{
    [Fact]
    public async Task RssFeedSource_NormalizesMatchingFeedItems()
    {
        const string xml = """
<rss>
  <channel>
    <item>
      <title>Election alpha polling update</title>
      <link>https://news.example.com/election-alpha</link>
      <description>Candidate alpha gained support today.</description>
      <pubDate>Mon, 04 May 2026 01:00:00 GMT</pubDate>
    </item>
    <item>
      <title>Unrelated sports note</title>
      <link>https://news.example.com/sports</link>
      <description>No market terms here.</description>
    </item>
  </channel>
</rss>
""";
        var source = new RssFeedSource(
            new HttpClient(new StaticHttpHandler(xml)),
            Options.Create(new OpportunityDiscoveryOptions
            {
                Rss = new RssEvidenceOptions
                {
                    Enabled = true,
                    FeedUrls = ["https://feed.example.com/rss"],
                    MaxItemsPerFeed = 10
                }
            }));

        var results = await source.SearchAsync(new EvidenceQuery(Guid.NewGuid(), Market(), 5));

        var item = Assert.Single(results);
        Assert.Equal(EvidenceSourceKind.Rss, item.SourceKind);
        Assert.Equal("https://news.example.com/election-alpha", item.Url);
        Assert.Contains("alpha", item.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GdeltDocApiSource_NormalizesArticles()
    {
        const string json = """
{
  "articles": [
    {
      "url": "https://gdelt.example.com/article",
      "title": "Election alpha report",
      "domain": "gdelt.example.com",
      "seendate": "20260504010000"
    }
  ]
}
""";
        var source = new GdeltDocApiSource(
            new HttpClient(new StaticHttpHandler(json)),
            Options.Create(new OpportunityDiscoveryOptions
            {
                Gdelt = new GdeltEvidenceOptions
                {
                    Enabled = true,
                    BaseUrl = "https://api.example.com/doc",
                    MaxRecords = 10
                }
            }));

        var results = await source.SearchAsync(new EvidenceQuery(Guid.NewGuid(), Market(), 5));

        var item = Assert.Single(results);
        Assert.Equal(EvidenceSourceKind.Gdelt, item.SourceKind);
        Assert.Equal("gdelt.example.com", item.SourceName);
        Assert.Equal("https://gdelt.example.com/article", item.Url);
        Assert.NotNull(item.PublishedAtUtc);
    }

    [Fact]
    public async Task OpenAiWebSearchSource_UsesResponsesWebSearchToolAndAnnotations()
    {
        const string apiKeyEnvVar = "AUTOTRADE_TEST_OPENAI_WEB_SEARCH_KEY";
        var previous = Environment.GetEnvironmentVariable(apiKeyEnvVar);
        Environment.SetEnvironmentVariable(apiKeyEnvVar, "test-key");
        try
        {
            const string json = """
{
  "output": [
    {
      "type": "message",
      "content": [
        {
          "type": "output_text",
          "text": "Alpha report\nCandidate alpha gained support.",
          "annotations": [
            {
              "type": "url_citation",
              "url": "https://source.example.com/article",
              "title": "Alpha source"
            }
          ]
        }
      ]
    }
  ]
}
""";
            var handler = new CaptureHttpHandler(json);
            var source = new OpenAiWebSearchSource(
                new HttpClient(handler),
                Options.Create(new OpportunityDiscoveryOptions
                {
                    OpenAiWebSearch = new OpenAiWebSearchOptions
                    {
                        Enabled = true,
                        BaseUrl = "https://api.openai.test/v1",
                        ApiKeyEnvVar = apiKeyEnvVar,
                        Model = "test-model",
                        MaxResults = 5
                    }
                }));

            var results = await source.SearchAsync(new EvidenceQuery(Guid.NewGuid(), Market(), 5));

            var item = Assert.Single(results);
            Assert.Equal(EvidenceSourceKind.OpenAiWebSearch, item.SourceKind);
            Assert.Equal("https://source.example.com/article", item.Url);
            Assert.Contains("\"type\":\"web_search\"", handler.RequestBody, StringComparison.Ordinal);
            Assert.Contains("\"tool_choice\":\"auto\"", handler.RequestBody, StringComparison.Ordinal);
            Assert.Contains("web_search_call.action.sources", handler.RequestBody, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(apiKeyEnvVar, previous);
        }
    }

    private static MarketInfoDto Market()
    {
        return new MarketInfoDto
        {
            MarketId = "market-1",
            ConditionId = "condition-1",
            Name = "Will candidate alpha win the election?",
            Status = "active",
            TokenIds = ["yes-token", "no-token"]
        };
    }

    private sealed class StaticHttpHandler : HttpMessageHandler
    {
        private readonly string _body;

        public StaticHttpHandler(string body)
        {
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class CaptureHttpHandler : HttpMessageHandler
    {
        private readonly string _body;

        public CaptureHttpHandler(string body)
        {
            _body = body;
        }

        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
        }
    }
}
