using Autotrade.Api.Controllers;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Tests;

public sealed class OpportunityControllerContractTests
{
    [Fact]
    public async Task GetScoreReturnsSharedOperatorContract()
    {
        var opportunityId = Guid.NewGuid();
        var controller = new OpportunityController(new StubOperatorService(opportunityId));

        var result = await controller.GetScore(opportunityId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<OpportunityScoreStatusResponse>(ok.Value);
        Assert.Equal(opportunityId, response.OpportunityId);
    }

    [Fact]
    public async Task SuspendReturnsSharedOperatorContract()
    {
        var opportunityId = Guid.NewGuid();
        var controller = new OpportunityController(new StubOperatorService(opportunityId));

        var result = await controller.Suspend(
            opportunityId,
            new OpportunityOperatorActionRequest("api-test", "manual suspension"),
            CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result.Result);
        var response = Assert.IsType<OpportunityOperatorSuspendResponse>(accepted.Value);
        Assert.True(response.Suspended);
        Assert.Equal(opportunityId, response.OpportunityId);
    }

    private sealed class StubOperatorService(Guid opportunityId) : IOpportunityOperatorService
    {
        public Task<OpportunityScoreStatusResponse> GetScoreAsync(
            Guid requestOpportunityId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new OpportunityScoreStatusResponse(requestOpportunityId, Hypothesis(requestOpportunityId), null, []));

        public Task<OpportunityReplayStatusResponse> GetReplayAsync(
            Guid requestOpportunityId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new OpportunityReplayStatusResponse(requestOpportunityId, Hypothesis(requestOpportunityId), [], [], null, null, []));

        public Task<OpportunityPromoteResponse> PromoteAsync(
            OpportunityPromoteRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new OpportunityPromoteResponse(request.OpportunityId, true, null, []));

        public Task<OpportunityLiveStatusResponse> GetLiveStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new OpportunityLiveStatusResponse(DateTimeOffset.UtcNow, [], [], [], []));

        public Task<OpportunityOperatorSuspendResponse> SuspendAsync(
            OpportunityOperatorSuspendRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new OpportunityOperatorSuspendResponse(request.OpportunityId, true, Hypothesis(request.OpportunityId), null, null, []));

        public Task<OpportunityExplainResponse> ExplainAsync(
            Guid requestOpportunityId,
            DateTimeOffset? asOfUtc = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new OpportunityExplainResponse(
                requestOpportunityId,
                DateTimeOffset.UtcNow,
                true,
                Hypothesis(requestOpportunityId),
                null,
                [],
                [],
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                [],
                [],
                null,
                null,
                [],
                [],
                []));

        private OpportunityHypothesisDto Hypothesis(Guid requestOpportunityId)
            => new(
                requestOpportunityId == Guid.Empty ? opportunityId : requestOpportunityId,
                Guid.NewGuid(),
                "market-1",
                Autotrade.Trading.Domain.Shared.Enums.OutcomeSide.Yes,
                Guid.NewGuid(),
                "tape:market-1",
                "prompt-v1",
                "model-v1",
                "score-v1",
                "seed-1",
                Autotrade.OpportunityDiscovery.Domain.Shared.Enums.OpportunityHypothesisStatus.LivePublished,
                "test",
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow);
    }
}
