using Autotrade.Api.Controllers;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Decisions;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Tests;

public sealed class StrategyDecisionsControllerContractTests
{
    [Fact]
    public async Task QueryReturnsRecentDecisionsWithIdsAndCorrelationIds()
    {
        var decision = CreateDecisionRecord();
        var service = new FakeStrategyDecisionQueryService
        {
            QueryRecordsResult = [decision]
        };
        var controller = new StrategyDecisionsController(service);
        var from = DateTimeOffset.UtcNow.AddHours(-1);
        var to = DateTimeOffset.UtcNow;

        var result = await controller.Query(
            strategyId: "strategy-main",
            marketId: "market-1",
            action: "RiskRejected",
            correlationId: "corr-1",
            runSessionId: null,
            fromUtc: from,
            toUtc: to,
            limit: 25,
            cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<StrategyDecisionListResponse>(ok.Value);
        Assert.Equal(1, response.Count);
        Assert.Equal(25, response.Limit);
        Assert.Equal(decision.DecisionId, response.Decisions[0].DecisionId);
        Assert.Equal("corr-1", response.Decisions[0].CorrelationId);
        Assert.Equal("RiskRejected", response.Decisions[0].Action);
        Assert.Equal("strategy-main", service.LastQuery?.StrategyId);
        Assert.Equal("market-1", service.LastQuery?.MarketId);
        Assert.Equal("RiskRejected", service.LastQuery?.Action);
        Assert.Equal("corr-1", service.LastQuery?.CorrelationId);
        Assert.Equal(from, service.LastQuery?.FromUtc);
        Assert.Equal(to, service.LastQuery?.ToUtc);
    }

    [Fact]
    public async Task GetDetailBuildsReasonChainFromDecisionContextJson()
    {
        var decision = CreateDecisionRecord();
        var service = new FakeStrategyDecisionQueryService
        {
            GetResult = decision
        };
        var controller = new StrategyDecisionsController(service);

        var result = await controller.GetDetail(decision.DecisionId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<StrategyDecisionDetailResponse>(ok.Value);
        Assert.Equal(decision.DecisionId, response.DecisionId);
        Assert.Equal("0.47", response.ReasonChain.SignalInputs["bestAsk"]);
        Assert.Equal("0.020", response.ReasonChain.Thresholds["minEdge"]);
        Assert.NotNull(response.ReasonChain.RiskVerdict);
        Assert.False(response.ReasonChain.RiskVerdict!.Allowed);
        Assert.Equal("MAX_OPEN_ORDERS", response.ReasonChain.RiskVerdict.Code);
        Assert.Single(response.ReasonChain.OrderReferences);
        Assert.Equal("order-1", response.ReasonChain.OrderReferences[0].ClientOrderId);
        Assert.Equal("corr-1", response.CorrelationId);
    }

    [Fact]
    public async Task GetDetailReturnsNotFoundForMissingDecision()
    {
        var controller = new StrategyDecisionsController(new FakeStrategyDecisionQueryService());

        var result = await controller.GetDetail(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    private static StrategyDecisionRecord CreateDecisionRecord()
    {
        const string contextJson = """
            {
              "signalInputs": {
                "bestAsk": 0.47,
                "liquidity": 12000
              },
              "thresholds": {
                "minEdge": "0.020"
              },
              "riskVerdict": {
                "allowed": false,
                "code": "MAX_OPEN_ORDERS",
                "message": "Open order limit reached."
              },
              "orderReferences": [
                {
                  "clientOrderId": "order-1",
                  "marketId": "market-1",
                  "side": "Buy",
                  "outcome": "YES",
                  "price": "0.47",
                  "quantity": "20",
                  "status": "Rejected"
                }
              ]
            }
            """;

        return new StrategyDecisionRecord(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "strategy-main",
            "RiskRejected",
            "Open order limit reached.",
            "market-1",
            contextJson,
            DateTimeOffset.Parse("2026-05-03T12:00:00Z"),
            "cfg-001",
            "corr-1",
            "Paper");
    }

    private sealed class FakeStrategyDecisionQueryService : IStrategyDecisionQueryService
    {
        public StrategyDecisionQuery? LastQuery { get; private set; }
        public IReadOnlyList<StrategyDecisionRecord> QueryRecordsResult { get; init; } = [];
        public StrategyDecisionRecord? GetResult { get; init; }

        public Task<IReadOnlyList<StrategyDecision>> QueryAsync(
            StrategyDecisionQuery query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<StrategyDecision>>([]);
        }

        public Task<IReadOnlyList<StrategyDecisionRecord>> QueryRecordsAsync(
            StrategyDecisionQuery query,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult(QueryRecordsResult);
        }

        public Task<StrategyDecisionRecord?> GetAsync(Guid decisionId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(GetResult?.DecisionId == decisionId ? GetResult : null);
        }
    }
}
