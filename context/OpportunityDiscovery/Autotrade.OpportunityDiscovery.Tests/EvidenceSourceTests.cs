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
    public async Task PolymarketAccountTradeSource_NormalizesPublicWalletTradesForMarket()
    {
        const string conditionId = "0x1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef";
        const string json = """
[
  {
    "conditionId": "0x1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef",
    "market": "market-1",
    "title": "Will candidate alpha win the election?",
    "outcome": "Yes",
    "side": "BUY",
    "price": 0.42,
    "size": 10,
    "timestamp": 1770000000,
    "transactionHash": "0xtx1"
  },
  {
    "conditionId": "0x1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef",
    "market": "market-1",
    "title": "Will candidate alpha win the election?",
    "outcome": "Yes",
    "side": "SELL",
    "price": "0.55",
    "size": "4",
    "timestamp": "2026-02-02T00:00:00Z",
    "transactionHash": "0xtx2"
  },
  {
    "conditionId": "0xother",
    "market": "other-market",
    "title": "Other market",
    "outcome": "No",
    "side": "BUY",
    "price": 0.30,
    "size": 1,
    "timestamp": 1770000000
  }
]
""";
        var handler = new CaptureHttpHandler(json);
        var source = new PolymarketAccountTradeSource(
            new HttpClient(handler),
            Options.Create(new OpportunityDiscoveryOptions
            {
                PolymarketAccounts = new PolymarketAccountEvidenceOptions
                {
                    Enabled = true,
                    BaseUrl = "https://data-api.example.com",
                    WalletAddresses = ["0xabc123abc123abc123abc123abc123abc123abcd"],
                    MaxTradesPerWallet = 10,
                    SourceQuality = 0.8m
                }
            }));
        var market = Market() with { ConditionId = conditionId };

        var results = await source.SearchAsync(new EvidenceQuery(Guid.NewGuid(), market, 5));

        var item = Assert.Single(results);
        Assert.Equal(EvidenceSourceKind.Polymarket, item.SourceKind);
        Assert.Contains("polymarket_account_trades", item.SourceName, StringComparison.Ordinal);
        Assert.Contains("/trades?", item.Url, StringComparison.Ordinal);
        Assert.Contains("market=", handler.RequestUri, StringComparison.Ordinal);
        Assert.Contains(conditionId, handler.RequestUri, StringComparison.Ordinal);
        Assert.Contains("\"tradeCount\":2", item.Summary, StringComparison.Ordinal);
        Assert.Contains("\"signedQuantity\":10", item.Summary, StringComparison.Ordinal);
        Assert.Contains("\"signedQuantity\":-4", item.Summary, StringComparison.Ordinal);
        Assert.Contains("0xtx1", item.RawJson, StringComparison.Ordinal);
        Assert.Equal(0.8m, item.SourceQuality);
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

        public string RequestUri { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString() ?? string.Empty;
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
