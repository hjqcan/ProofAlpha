using System.Text.Json;
using Autotrade.Api.Controllers;
using Autotrade.ArcSettlement.Application.Contract.Access;
using Autotrade.ArcSettlement.Application.Contract.Proofs;
using Autotrade.ArcSettlement.Application.Contract.Signals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Tests;

public sealed class ArcSignalsControllerContractTests
{
    [Fact]
    public async Task ListReturnsPublicSignalSummariesWithoutSensitiveProofMaterial()
    {
        var service = new FakeArcSignalPublicationService
        {
            ListResult = [CreateRecord()]
        };
        var controller = CreateController(service);

        var result = await controller.List(limit: 10, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var records = Assert.IsAssignableFrom<IReadOnlyList<ArcSignalSummaryResponse>>(ok.Value);
        Assert.Single(records);
        Assert.Equal("0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", records[0].SignalId);
        Assert.Equal(Hash("provenance"), records[0].ProvenanceHash);
        Assert.Equal(10, service.LastQuery?.Limit);

        var json = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("privateKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ARC_SETTLEMENT_PRIVATE_KEY", json, StringComparison.Ordinal);
        Assert.DoesNotContain("apiSecret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reasoningHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("riskEnvelopeHash", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetReturnsNotFoundForMissingSignal()
    {
        var access = new FakeArcAccessDecisionService();
        var controller = CreateController(new FakeArcSignalPublicationService(), access);

        var result = await controller.Get(
            "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            walletAddress: Wallet,
            cancellationToken: CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Empty(access.Requests);
    }

    [Fact]
    public async Task GetDeniesSignalDetailWhenWalletHasNoSubscription()
    {
        var service = new FakeArcSignalPublicationService { GetResult = CreateRecord() };
        var access = new FakeArcAccessDecisionService
        {
            Result = new ArcAccessDecision(
                Allowed: false,
                "ACCESS_NOT_FOUND",
                "No active Arc subscription entitlement was found for this wallet and strategy.",
                ArcEntitlementPermission.ViewSignals,
                StrategyKey,
                Wallet,
                "arc-signal",
                SignalId)
        };
        var controller = CreateController(service, access);

        var result = await controller.Get(SignalId, walletAddress: Wallet, cancellationToken: CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        var decision = Assert.IsType<ArcAccessDecision>(forbidden.Value);
        Assert.False(decision.Allowed);
        Assert.Equal("ACCESS_NOT_FOUND", decision.ReasonCode);
        Assert.Equal(ArcEntitlementPermission.ViewSignals, access.Requests[0].RequiredPermission);
        Assert.Equal(StrategyKey, access.Requests[0].StrategyKey);
        Assert.Equal(Wallet, access.Requests[0].WalletAddress);
    }

    [Fact]
    public async Task GetAllowsSignalDetailWhenWalletHasViewSignalsPermission()
    {
        var record = CreateRecord();
        var service = new FakeArcSignalPublicationService { GetResult = record };
        var access = new FakeArcAccessDecisionService { Result = CreateAllowedDecision() };
        var controller = CreateController(service, access);

        var result = await controller.Get(SignalId, walletAddress: Wallet, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ArcSignalDetailResponse>(ok.Value);
        Assert.Equal(record.SignalId, response.SignalId);
        Assert.Equal(record.ReasoningHash, response.ReasoningHash);
        Assert.Equal(record.RiskEnvelopeHash, response.RiskEnvelopeHash);
        Assert.Equal(record.ProvenanceHash, response.ProvenanceHash);
        Assert.Equal(record.EvidenceUri, response.EvidenceUri);
        Assert.Equal("arc-signal", access.Requests[0].ResourceKind);
        Assert.Equal(SignalId, access.Requests[0].ResourceId);
        Assert.Equal(ArcEntitlementPermission.ViewReasoning, access.Requests[1].RequiredPermission);
    }

    [Fact]
    public async Task GetOmitsReasoningHashesWhenWalletLacksViewReasoningPermission()
    {
        var record = CreateRecord();
        var service = new FakeArcSignalPublicationService { GetResult = record };
        var access = new FakeArcAccessDecisionService(
            CreateAllowedDecision(),
            new ArcAccessDecision(
                Allowed: false,
                "MISSING_PERMISSION",
                "Wallet entitlement does not include ViewReasoning.",
                ArcEntitlementPermission.ViewReasoning,
                StrategyKey,
                Wallet,
                "arc-signal-reasoning",
                SignalId));
        var controller = CreateController(service, access);

        var result = await controller.Get(SignalId, walletAddress: Wallet, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ArcSignalDetailResponse>(ok.Value);
        Assert.Null(response.ReasoningHash);
        Assert.Null(response.RiskEnvelopeHash);
        Assert.False(response.ReasoningDecision.Allowed);
    }

    [Fact]
    public async Task GetReadsDemoWalletFromHeaderWhenQueryIsMissing()
    {
        var record = CreateRecord();
        var service = new FakeArcSignalPublicationService { GetResult = record };
        var access = new FakeArcAccessDecisionService { Result = CreateAllowedDecision() };
        var controller = CreateController(service, access);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.ControllerContext.HttpContext.Request.Headers["X-Arc-Wallet"] = Wallet;

        var result = await controller.Get(SignalId, walletAddress: null, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ArcSignalDetailResponse>(ok.Value);
        Assert.Equal(record.SignalId, response.SignalId);
        Assert.All(access.Requests, request => Assert.Equal(Wallet, request.WalletAddress));
    }

    [Fact]
    public async Task PublishRequiresActorAndReason()
    {
        var controller = CreateController(new FakeArcSignalPublicationService());

        var result = await controller.Publish(
            new PublishArcSignalRequest(
                CreateProof(),
                ArcSignalSourceReviewStatus.Approved,
                Actor: "",
                Reason: "publish for demo"),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task PublishForwardsRequestToApplicationService()
    {
        var record = CreateRecord();
        var service = new FakeArcSignalPublicationService
        {
            PublishResult = new ArcSignalPublicationResult(record, AlreadyExisted: false)
        };
        var controller = CreateController(service);
        var request = new PublishArcSignalRequest(
            CreateProof(),
            ArcSignalSourceReviewStatus.Approved,
            "operator-1",
            "publish phase 3 demo");

        var result = await controller.Publish(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ArcSignalPublicationResult>(ok.Value);
        Assert.Equal(record.SignalId, response.Record.SignalId);
        Assert.Same(request, service.LastPublishRequest);
    }

    [Fact]
    public async Task PublishSourceForwardsSourceRequestToApplicationService()
    {
        var record = CreateRecord();
        var service = new FakeArcSignalPublicationService
        {
            PublishFromSourceResult = new ArcSignalPublicationResult(record, AlreadyExisted: false)
        };
        var controller = CreateController(service);
        var request = new PublishArcSignalSourceRequest(
            ArcProofSourceKind.Opportunity,
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            "operator-1",
            "publish reviewed opportunity");

        var result = await controller.PublishSource(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ArcSignalPublicationResult>(ok.Value);
        Assert.Equal(record.SignalId, response.Record.SignalId);
        Assert.Same(request, service.LastPublishFromSourceRequest);
    }

    [Fact]
    public async Task PublishSourceMapsMissingSourceToNotFound()
    {
        var service = new FakeArcSignalPublicationService
        {
            PublishFromSourceException = new ArcSignalSourceResolutionException(
                "SOURCE_NOT_FOUND",
                "Source was not found.")
        };
        var controller = CreateController(service);

        var result = await controller.PublishSource(
            new PublishArcSignalSourceRequest(
                ArcProofSourceKind.Opportunity,
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                "operator-1",
                "publish reviewed opportunity"),
            CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    private const string SignalId = "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Wallet = "0x1234567890abcdef1234567890abcdef12345678";
    private const string StrategyKey = "repricing_lag_arbitrage";

    private static ArcSignalsController CreateController(
        FakeArcSignalPublicationService service,
        FakeArcAccessDecisionService? accessDecisionService = null)
        => new(
            service,
            accessDecisionService
            ?? new FakeArcAccessDecisionService(
                CreateAllowedDecision(),
                CreateAllowedDecision(ArcEntitlementPermission.ViewReasoning, "arc-signal-reasoning")));

    private static ArcSignalPublicationRecord CreateRecord()
        => new(
            SignalId,
            ArcProofSourceKind.Opportunity,
            "demo-opportunity-arc-phase-3",
            "0x70997970c51812dc3a010c7d01b50e0d17dc79c8",
            StrategyKey,
            "demo-polymarket-market",
            "polymarket",
            Hash("reasoning"),
            Hash("risk"),
            42m,
            100m,
            DateTimeOffset.Parse("2027-01-15T08:00:00Z"),
            ArcSignalPublicationStatus.Confirmed,
            Hash("signal"),
            SourcePolicyHash: null,
            TransactionHash: "0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            ExplorerUrl: "https://explorer.arc.test/tx/0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            ErrorCode: null,
            CreatedAtUtc: DateTimeOffset.Parse("2026-05-11T17:53:52Z"),
            PublishedAtUtc: DateTimeOffset.Parse("2026-05-11T17:54:00Z"),
            Actor: "operator-1",
            Reason: "publish phase 3 demo",
            ProvenanceHash: Hash("provenance"),
            EvidenceUri: "artifacts/arc-hackathon/demo-run/provenance/opportunity-phase8-1.json");

    private static ArcStrategySignalProofDocument CreateProof()
        => new(
            "arc-strategy-signal-proof.v1",
            "0x70997970c51812dc3a010c7d01b50e0d17dc79c8",
            ArcProofSourceKind.Opportunity,
            "demo-opportunity-arc-phase-3",
            "repricing_lag_arbitrage",
            "demo-polymarket-market",
            "polymarket",
            DateTimeOffset.Parse("2026-05-11T17:53:52Z"),
            "phase-3-demo",
            ["demo-ev-orderbook", "demo-ev-risk-envelope"],
            Hash("opportunity"),
            Hash("reasoning"),
            Hash("risk"),
            42m,
            100m,
            DateTimeOffset.Parse("2027-01-15T08:00:00Z"));

    private static string Hash(string value)
        => $"0x{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant()}";

    private static ArcAccessDecision CreateAllowedDecision(
        ArcEntitlementPermission permission = ArcEntitlementPermission.ViewSignals,
        string resourceKind = "arc-signal")
        => new(
            Allowed: true,
            "ACCESS_ALLOWED",
            "Access allowed by active Arc subscription entitlement.",
            permission,
            StrategyKey,
            Wallet,
            resourceKind,
            SignalId,
            Tier: "SignalViewer",
            ExpiresAtUtc: DateTimeOffset.Parse("2026-05-19T00:00:00Z"),
            EvidenceTransactionHash: "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

    private sealed class FakeArcAccessDecisionService(
        params ArcAccessDecision[] decisions) : IArcAccessDecisionService
    {
        private readonly Queue<ArcAccessDecision> _decisions = new(decisions);

        public ArcAccessDecisionRequest? LastRequest { get; private set; }

        public List<ArcAccessDecisionRequest> Requests { get; } = [];

        public ArcAccessDecision Result { get; init; } = CreateAllowedDecision();

        public Task<ArcAccessDecision> EvaluateAsync(
            ArcAccessDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            Requests.Add(request);
            var decision = _decisions.Count > 0 ? _decisions.Dequeue() : Result;
            return Task.FromResult(
                decision with
                {
                    RequiredPermission = request.RequiredPermission,
                    StrategyKey = request.StrategyKey,
                    WalletAddress = request.WalletAddress ?? decision.WalletAddress,
                    ResourceKind = request.ResourceKind,
                    ResourceId = request.ResourceId
                });
        }
    }

    private sealed class FakeArcSignalPublicationService : IArcSignalPublicationService
    {
        public ArcSignalPublicationQuery? LastQuery { get; private set; }

        public PublishArcSignalRequest? LastPublishRequest { get; private set; }

        public PublishArcSignalSourceRequest? LastPublishFromSourceRequest { get; private set; }

        public IReadOnlyList<ArcSignalPublicationRecord> ListResult { get; init; } = [];

        public ArcSignalPublicationRecord? GetResult { get; init; }

        public ArcSignalPublicationResult? PublishResult { get; init; }

        public ArcSignalPublicationResult? PublishFromSourceResult { get; init; }

        public ArcSignalSourceResolutionException? PublishFromSourceException { get; init; }

        public Task<ArcSignalPublicationResult> PublishAsync(
            PublishArcSignalRequest request,
            CancellationToken cancellationToken = default)
        {
            LastPublishRequest = request;
            return Task.FromResult(PublishResult ?? new ArcSignalPublicationResult(CreateRecord(), AlreadyExisted: false));
        }

        public Task<ArcSignalPublicationResult> PublishFromSourceAsync(
            PublishArcSignalSourceRequest request,
            CancellationToken cancellationToken = default)
        {
            LastPublishFromSourceRequest = request;
            if (PublishFromSourceException is not null)
            {
                throw PublishFromSourceException;
            }

            return Task.FromResult(PublishFromSourceResult ?? new ArcSignalPublicationResult(CreateRecord(), AlreadyExisted: false));
        }

        public Task<IReadOnlyList<ArcSignalPublicationRecord>> ListAsync(
            ArcSignalPublicationQuery query,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult(ListResult);
        }

        public Task<ArcSignalPublicationRecord?> GetAsync(
            string signalId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(GetResult);
    }
}
