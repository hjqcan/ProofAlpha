using System.Text.Json;
using System.Text.Json.Serialization;
using Autotrade.Llm;
using Autotrade.SelfImprove.Application.Contract.Episodes;
using Autotrade.SelfImprove.Application.Contract.Llm;
using Autotrade.SelfImprove.Application.Llm;

namespace Autotrade.SelfImprove.Tests.Llm;

public sealed class OpenAiCompatibleLlmClientTests
{
    [Fact]
    public async Task AnalyzeEpisodeAsync_UsesSharedJsonClientAndMapsProposals()
    {
        var client = new OpenAiCompatibleLlmClient(new StaticJsonClient("""
{
  "proposals": [
    {
      "kind": "parameterPatch",
      "riskLevel": "low",
      "title": "Tighten spread",
      "rationale": "Observed rejects",
      "evidence": [
        {
          "source": "observation",
          "id": "obs-1"
        }
      ],
      "expectedImpact": "fewer rejects",
      "rollbackConditions": [
        "rejects rise"
      ],
      "parameterPatches": [
        {
          "path": "Strategies:LiquidityPulse:MaxSpread",
          "valueJson": "0.04",
          "reason": "wide spread",
          "maxRelativeChange": 0.2
        }
      ],
      "generatedStrategy": null
    }
  ]
}
"""));

        var proposals = await client.AnalyzeEpisodeAsync(CreateRequest());

        Assert.Single(proposals);
        Assert.Equal("Tighten spread", proposals[0].Title);
        Assert.Single(proposals[0].Evidence);
    }

    [Fact]
    public async Task AnalyzeEpisodeAsync_PropagatesSharedLlmFailure()
    {
        var client = new OpenAiCompatibleLlmClient(
            new ThrowingJsonClient(new LlmClientException("malformed payload")));

        await Assert.ThrowsAsync<LlmClientException>(() => client.AnalyzeEpisodeAsync(CreateRequest()));
    }

    private static StrategyEpisodeAnalysisRequest CreateRequest()
    {
        return new StrategyEpisodeAnalysisRequest(
            new StrategyEpisodeDto(
                Guid.NewGuid(),
                "liquidity_pulse",
                null,
                "v1",
                DateTimeOffset.UtcNow.AddHours(-1),
                DateTimeOffset.UtcNow,
                1,
                1,
                1,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                "{\"observations\":[\"obs-1\"]}",
                "{}",
                DateTimeOffset.UtcNow),
            "{}",
            "{}");
    }

    private sealed class StaticJsonClient : ILlmJsonClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        private readonly string _json;

        public StaticJsonClient(string json)
        {
            _json = json;
        }

        public Task<LlmJsonResult<T>> CompleteJsonAsync<T>(
            LlmJsonRequest request,
            Func<T, IReadOnlyList<string>>? validator = null,
            CancellationToken cancellationToken = default)
            where T : class
        {
            var value = JsonSerializer.Deserialize<T>(_json, JsonOptions)
                ?? throw new LlmClientException("test json did not deserialize");
            return Task.FromResult(new LlmJsonResult<T>(value, _json, _json));
        }
    }

    private sealed class ThrowingJsonClient : ILlmJsonClient
    {
        private readonly Exception _exception;

        public ThrowingJsonClient(Exception exception)
        {
            _exception = exception;
        }

        public Task<LlmJsonResult<T>> CompleteJsonAsync<T>(
            LlmJsonRequest request,
            Func<T, IReadOnlyList<string>>? validator = null,
            CancellationToken cancellationToken = default)
            where T : class
        {
            return Task.FromException<LlmJsonResult<T>>(_exception);
        }
    }
}
