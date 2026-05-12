using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.SelfImprove.Application.Python;
using Autotrade.Strategy.Application.Contract.Strategies;

namespace Autotrade.SelfImprove.Tests.GeneratedStrategies;

public sealed class PythonStrategyAdapterTests
{
    [Fact]
    public async Task EvaluateEntryAsync_BlocksIntentAboveGeneratedRiskEnvelope()
    {
        var observations = new CapturingObservationLogger();
        var adapter = new PythonStrategyAdapter(
            CreateManifest(),
            new StrategyContext
            {
                StrategyId = "generated_probe",
                ExecutionService = null!,
                OrderBookReader = null!,
                MarketCatalog = null!,
                RiskManager = null!,
                DecisionLogger = null!,
                ObservationLogger = observations
            },
            new StaticPythonRuntime(new PythonStrategyResponse(
                "enter",
                "test_enter",
                "test entry",
                new[]
                {
                    new PythonOrderIntent(
                        "market-1",
                        "token-yes",
                        "Yes",
                        "Buy",
                        "Limit",
                        "Gtc",
                        6m,
                        1m,
                        false,
                        "Single")
                },
                new Dictionary<string, object?>(),
                new Dictionary<string, object?>())));

        var signal = await adapter.EvaluateEntryAsync(CreateSnapshot());

        Assert.Null(signal);
        var observation = Assert.Single(observations.Observations);
        Assert.Equal("Blocked", observation.Outcome);
        Assert.Equal("generated_risk_envelope", observation.ReasonCode);
    }

    private static PythonStrategyManifest CreateManifest()
    {
        return new PythonStrategyManifest(
            "generated_probe",
            "Generated Probe",
            "v1",
            "v1",
            "artifacts/self-improve/generated_probe/v1",
            "hash",
            "strategy.py:evaluate",
            "{}",
            """
{
  "maxSingleOrderNotional": 5,
  "maxCycleNotional": 20,
  "maxTotalNotional": 100
}
""",
            new Dictionary<string, object?>());
    }

    private static MarketSnapshot CreateSnapshot()
    {
        return new MarketSnapshot(
            new MarketInfoDto
            {
                MarketId = "market-1",
                ConditionId = "condition-1",
                Name = "Test market",
                Status = "active"
            },
            null,
            null,
            DateTimeOffset.UtcNow);
    }

    private sealed class StaticPythonRuntime : IPythonStrategyRuntime
    {
        private readonly PythonStrategyResponse _response;

        public StaticPythonRuntime(PythonStrategyResponse response)
        {
            _response = response;
        }

        public Task<PythonStrategyResponse> EvaluateAsync(
            PythonStrategyManifest manifest,
            PythonStrategyRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_response);
        }
    }

    private sealed class CapturingObservationLogger : IStrategyObservationLogger
    {
        public List<StrategyObservation> Observations { get; } = new();

        public Task LogAsync(StrategyObservation observation, CancellationToken cancellationToken = default)
        {
            Observations.Add(observation);
            return Task.CompletedTask;
        }

        public Task FlushAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
