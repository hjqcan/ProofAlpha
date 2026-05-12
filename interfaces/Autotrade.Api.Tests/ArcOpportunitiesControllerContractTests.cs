using System.Text.Json;
using Autotrade.Api.Controllers;
using Autotrade.ArcSettlement.Application.Contract.Access;
using Autotrade.ArcSettlement.Application.Contract.Configuration;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Tests;

public sealed class ArcOpportunitiesControllerContractTests
{
    private static readonly Guid OpportunityId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ResearchRunId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid EvidenceId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private const string Wallet = "0x1234567890abcdef1234567890abcdef12345678";
    private const string StrategyKey = "llm_opportunity";

    [Fact]
    public async Task ListReturnsPublicSummariesWithoutReasoningOrPolicyMaterial()
    {
        var query = new FakeOpportunityQueryService
        {
            Opportunities = [CreateOpportunity()]
        };
        var controller = CreateController(query);

        var result = await controller.List(status: "Approved", limit: 10, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsAssignableFrom<IReadOnlyList<ArcOpportunitySummaryResponse>>(ok.Value);
        Assert.Single(response);
        Assert.Equal(OpportunityId, response[0].OpportunityId);
        Assert.Equal(OpportunityStatus.Approved, query.LastStatus);
        Assert.Equal(10, query.LastLimit);

        var json = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("reason", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("compiledPolicy", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evidence", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetDeniesOpportunityDetailWithoutViewSignalsPermission()
    {
        var query = new FakeOpportunityQueryService { Opportunity = CreateOpportunity() };
        var access = new FakeArcAccessDecisionService(
            CreateDecision(allowed: false, ArcEntitlementPermission.ViewSignals, "ACCESS_NOT_FOUND"));
        var controller = CreateController(query, access);

        var result = await controller.Get(OpportunityId, walletAddress: Wallet, CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        var decision = Assert.IsType<ArcAccessDecision>(forbidden.Value);
        Assert.False(decision.Allowed);
        Assert.Equal(ArcEntitlementPermission.ViewSignals, access.Requests[0].RequiredPermission);
        Assert.Equal(StrategyKey, access.Requests[0].StrategyKey);
    }

    [Fact]
    public async Task GetOmitsReasoningAndEvidenceWithoutViewReasoningPermission()
    {
        var query = new FakeOpportunityQueryService
        {
            Opportunity = CreateOpportunity(),
            Evidence = [CreateEvidence()]
        };
        var access = new FakeArcAccessDecisionService(
            CreateDecision(allowed: true, ArcEntitlementPermission.ViewSignals, "ACCESS_ALLOWED"),
            CreateDecision(allowed: false, ArcEntitlementPermission.ViewReasoning, "MISSING_PERMISSION"));
        var controller = CreateController(query, access);

        var result = await controller.Get(OpportunityId, walletAddress: Wallet, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ArcOpportunityDetailResponse>(ok.Value);
        Assert.Equal("{}", response.CompiledPolicyJson);
        Assert.Null(response.Reason);
        Assert.Null(response.ScoreJson);
        Assert.Empty(response.Evidence);
        Assert.False(response.ReasoningDecision.Allowed);
        Assert.Equal(0, query.GetEvidenceCallCount);
    }

    [Fact]
    public async Task GetIncludesReasoningAndEvidenceWithViewReasoningPermissionFromHeaderWallet()
    {
        var query = new FakeOpportunityQueryService
        {
            Opportunity = CreateOpportunity(),
            Evidence = [CreateEvidence()]
        };
        var access = new FakeArcAccessDecisionService(
            CreateDecision(allowed: true, ArcEntitlementPermission.ViewSignals, "ACCESS_ALLOWED"),
            CreateDecision(allowed: true, ArcEntitlementPermission.ViewReasoning, "ACCESS_ALLOWED"));
        var controller = CreateController(query, access);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.ControllerContext.HttpContext.Request.Headers["X-Arc-Wallet"] = Wallet;

        var result = await controller.Get(OpportunityId, walletAddress: null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ArcOpportunityDetailResponse>(ok.Value);
        Assert.Equal("Evidence-backed mispricing.", response.Reason);
        Assert.Equal("""{"modelScore":0.91}""", response.ScoreJson);
        Assert.Single(response.Evidence);
        Assert.Equal(Wallet, access.Requests[0].WalletAddress);
        Assert.Equal(Wallet, access.Requests[1].WalletAddress);
    }

    private static ArcOpportunitiesController CreateController(
        FakeOpportunityQueryService query,
        FakeArcAccessDecisionService? access = null)
        => new(
            query,
            access ?? new FakeArcAccessDecisionService(
                CreateDecision(allowed: true, ArcEntitlementPermission.ViewSignals, "ACCESS_ALLOWED"),
                CreateDecision(allowed: true, ArcEntitlementPermission.ViewReasoning, "ACCESS_ALLOWED")),
            new TestOptionsMonitor<ArcSettlementOptions>(
                new ArcSettlementOptions
                {
                    SignalProof = new ArcSettlementSignalProofOptions
                    {
                        OpportunityStrategyId = StrategyKey
                    }
                }));

    private static MarketOpportunityDto CreateOpportunity()
        => new(
            OpportunityId,
            ResearchRunId,
            "market-1",
            OutcomeSide.Yes,
            FairProbability: 0.58m,
            Confidence: 0.71m,
            Edge: 0.045m,
            OpportunityStatus.Approved,
            DateTimeOffset.Parse("2026-05-12T11:00:00Z"),
            "Evidence-backed mispricing.",
            JsonSerializer.Serialize(new[] { EvidenceId }),
            """{"modelScore":0.91}""",
            "{}",
            DateTimeOffset.Parse("2026-05-12T10:00:00Z"),
            DateTimeOffset.Parse("2026-05-12T10:05:00Z"));

    private static EvidenceItemDto CreateEvidence()
        => new(
            EvidenceId,
            ResearchRunId,
            EvidenceSourceKind.Polymarket,
            "polymarket-orderbook",
            "https://polymarket.example/market-1",
            "market-1 order book",
            "Order book showed positive edge.",
            DateTimeOffset.Parse("2026-05-12T09:58:00Z"),
            DateTimeOffset.Parse("2026-05-12T10:00:00Z"),
            "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            0.95m);

    private static ArcAccessDecision CreateDecision(
        bool allowed,
        ArcEntitlementPermission permission,
        string reasonCode)
        => new(
            allowed,
            reasonCode,
            allowed
                ? "Access allowed by active Arc subscription entitlement."
                : "Arc access was denied.",
            permission,
            StrategyKey,
            Wallet,
            permission == ArcEntitlementPermission.ViewReasoning
                ? "arc-opportunity-reasoning"
                : "arc-opportunity",
            OpportunityId.ToString("D"),
            Tier: allowed ? "SignalViewer" : null,
            ExpiresAtUtc: allowed ? DateTimeOffset.Parse("2026-05-19T00:00:00Z") : null,
            EvidenceTransactionHash: allowed ? "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" : null);

    private sealed class FakeOpportunityQueryService : IOpportunityQueryService
    {
        public OpportunityStatus? LastStatus { get; private set; }

        public int LastLimit { get; private set; }

        public int GetEvidenceCallCount { get; private set; }

        public IReadOnlyList<MarketOpportunityDto> Opportunities { get; init; } = [];

        public MarketOpportunityDto? Opportunity { get; init; }

        public IReadOnlyList<EvidenceItemDto> Evidence { get; init; } = [];

        public Task<IReadOnlyList<MarketOpportunityDto>> ListOpportunitiesAsync(
            OpportunityStatus? status,
            int limit = 50,
            CancellationToken cancellationToken = default)
        {
            LastStatus = status;
            LastLimit = limit;
            return Task.FromResult(Opportunities);
        }

        public Task<MarketOpportunityDto?> GetOpportunityAsync(
            Guid opportunityId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Opportunity?.Id == opportunityId ? Opportunity : null);

        public Task<IReadOnlyList<EvidenceItemDto>> GetEvidenceAsync(
            Guid opportunityId,
            CancellationToken cancellationToken = default)
        {
            GetEvidenceCallCount++;
            return Task.FromResult(Evidence);
        }
    }

    private sealed class FakeArcAccessDecisionService(
        params ArcAccessDecision[] decisions) : IArcAccessDecisionService
    {
        private readonly Queue<ArcAccessDecision> _decisions = new(decisions);

        public List<ArcAccessDecisionRequest> Requests { get; } = [];

        public Task<ArcAccessDecision> EvaluateAsync(
            ArcAccessDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(
                _decisions.Count > 0
                    ? _decisions.Dequeue()
                    : CreateDecision(allowed: true, request.RequiredPermission, "ACCESS_ALLOWED"));
        }
    }
}
