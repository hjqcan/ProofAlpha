using Autotrade.Api.Controllers;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Tests;

public sealed class OpportunityControllerContractTests
{
    [Fact]
    public async Task GetScoreReturnsSharedOperatorContract()
    {
        var opportunityId = Guid.NewGuid();
        var controller = CreateController(opportunityId);

        var result = await controller.GetScore(opportunityId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<OpportunityScoreStatusResponse>(ok.Value);
        Assert.Equal(opportunityId, response.OpportunityId);
    }

    [Fact]
    public async Task SuspendReturnsSharedOperatorContract()
    {
        var opportunityId = Guid.NewGuid();
        var controller = CreateController(opportunityId);

        var result = await controller.Suspend(
            opportunityId,
            new OpportunityOperatorActionRequest("api-test", "manual suspension"),
            CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result.Result);
        var response = Assert.IsType<OpportunityOperatorSuspendResponse>(accepted.Value);
        Assert.True(response.Suspended);
        Assert.Equal(opportunityId, response.OpportunityId);
    }

    [Fact]
    public async Task IngestUserMessageReturnsSharedDiscoveryContract()
    {
        var opportunityId = Guid.NewGuid();
        var controller = CreateController(opportunityId);

        var result = await controller.IngestUserMessage(
            new OpportunityUserMessageIngestionRequest(
                "operator-note",
                "Local source",
                "User supplied market evidence"),
            CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result.Result);
        var response = Assert.IsType<OpportunityUserMessageIngestionResult>(accepted.Value);
        Assert.Equal(EvidenceSourceKind.Manual, response.Evidence.SourceKind);
        Assert.Equal("operator-note", response.Evidence.SourceName);
    }

    [Fact]
    public async Task IngestAccountActivityReturnsSharedDiscoveryContract()
    {
        var opportunityId = Guid.NewGuid();
        var controller = CreateController(opportunityId);

        var result = await controller.IngestAccountActivity(
            new OpportunityAccountActivityIngestionRequest(
                "0xabc123abc123abc123abc123abc123abc123abcd",
                [
                    new OpportunityAccountActivityEntry(
                        "market-alpha",
                        OutcomeSide.Yes,
                        OrderSide.Buy,
                        0.42m,
                        10m,
                        DateTimeOffset.UtcNow.AddMinutes(-1))
                ]),
            CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result.Result);
        var response = Assert.IsType<OpportunityAccountActivityIngestionResult>(accepted.Value);
        Assert.Equal(EvidenceSourceKind.Polymarket, response.Evidence.SourceKind);
        Assert.Equal("public-account-activity", response.Evidence.SourceName);
        Assert.Contains("\"activityCount\":1", response.SummaryJson, StringComparison.Ordinal);
    }

    private static OpportunityController CreateController(Guid opportunityId)
        => new(new StubOperatorService(opportunityId), new StubDiscoveryService());

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

    private sealed class StubDiscoveryService : IOpportunityDiscoveryService
    {
        public Task<OpportunityScanResult> ScanAsync(
            OpportunityScanRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new OpportunityScanResult(Run(), []));

        public Task<OpportunityUserMessageIngestionResult> IngestUserMessageAsync(
            OpportunityUserMessageIngestionRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new OpportunityUserMessageIngestionResult(
                Run(),
                new EvidenceItemDto(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    EvidenceSourceKind.Manual,
                    request.SourceName,
                    request.Url ?? "manual://user-message/test",
                    request.Title,
                    request.Message,
                    request.PublishedAtUtc,
                    DateTimeOffset.UtcNow,
                    "hash",
                    request.SourceQuality)));

        public Task<OpportunityAccountActivityIngestionResult> IngestPolymarketAccountActivityAsync(
            OpportunityAccountActivityIngestionRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new OpportunityAccountActivityIngestionResult(
                Run("polymarket-account:api-test"),
                new EvidenceItemDto(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    EvidenceSourceKind.Polymarket,
                    request.SourceName,
                    request.Url ?? "polymarket://account/test/hash",
                    $"Public Polymarket account activity for {request.WalletAddress}",
                    "account activity summary",
                    request.Activities.Max(activity => activity.ExecutedAtUtc),
                    request.ObservedAtUtc ?? DateTimeOffset.UtcNow,
                    "hash",
                    request.SourceQuality),
                $"{{\"activityCount\":{request.Activities.Count}}}"));

        public Task<MarketOpportunityDto> ApproveAsync(
            OpportunityReviewRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<MarketOpportunityDto> RejectAsync(
            OpportunityReviewRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<MarketOpportunityDto> PublishAsync(
            OpportunityReviewRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> ExpireStaleAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        private static ResearchRunDto Run(string trigger = "user-message:api-test")
            => new(
                Guid.NewGuid(),
                trigger,
                "[]",
                ResearchRunStatus.Succeeded,
                1,
                0,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow);
    }
}
