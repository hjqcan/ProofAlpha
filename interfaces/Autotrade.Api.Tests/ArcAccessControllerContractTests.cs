using Autotrade.Api.Controllers;
using Autotrade.ArcSettlement.Application.Contract.Access;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Tests;

public sealed class ArcAccessControllerContractTests
{
    [Fact]
    public void PlansReturnsConfiguredPlansWithoutSecretMaterial()
    {
        var plans = new FakeArcSubscriptionPlanService(
            [
                new ArcSubscriptionPlan(
                    1,
                    "repricing_lag_arbitrage",
                    "Signal Viewer",
                    "SignalViewer",
                    10m,
                    604800,
                    [ArcEntitlementPermission.ViewSignals, ArcEntitlementPermission.ViewReasoning],
                    MaxMarkets: 12,
                    AutoTradingAllowed: false,
                    LiveTradingAllowed: false,
                    CreatedAtUtc: DateTimeOffset.Parse("2026-05-11T00:00:00Z"))
            ]);
        var controller = new ArcAccessController(plans, new FakeArcStrategyAccessReader(), new FakeArcSubscriptionSyncService());

        var result = controller.Plans();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsAssignableFrom<IReadOnlyList<ArcSubscriptionPlan>>(ok.Value);
        var plan = Assert.Single(response);
        Assert.Equal("SignalViewer", plan.Tier);

        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.DoesNotContain("privateKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ARC_SETTLEMENT_PRIVATE_KEY", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetForwardsWalletAndStrategyToAccessReader()
    {
        var expected = new ArcStrategyAccessStatus(
            "0x1234567890abcdef1234567890abcdef12345678",
            "repricing_lag_arbitrage",
            ArcStrategyAccessStatusCode.Active,
            HasAccess: true,
            "ACCESS_ACTIVE",
            [ArcEntitlementPermission.ViewSignals],
            DateTimeOffset.Parse("2026-05-12T00:00:00Z"),
            Tier: "SignalViewer",
            ExpiresAtUtc: DateTimeOffset.Parse("2026-05-19T00:00:00Z"),
            SourceTransactionHash: "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            SyncedAtUtc: DateTimeOffset.Parse("2026-05-12T00:00:00Z"));
        var reader = new FakeArcStrategyAccessReader { Result = expected };
        var controller = new ArcAccessController(new FakeArcSubscriptionPlanService([]), reader, new FakeArcSubscriptionSyncService());

        var result = await controller.Get(
            "0x1234567890ABCDEF1234567890abcdef12345678",
            "repricing_lag_arbitrage",
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(expected, ok.Value);
        Assert.Equal("0x1234567890ABCDEF1234567890abcdef12345678", reader.LastWalletAddress);
        Assert.Equal("repricing_lag_arbitrage", reader.LastStrategyKey);
    }

    [Fact]
    public async Task SyncForwardsRequestToApplicationService()
    {
        var request = new SyncArcAccessRequest(
            "0x1234567890abcdef1234567890abcdef12345678",
            "repricing_lag_arbitrage",
            1,
            "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            DateTimeOffset.Parse("2026-05-19T00:00:00Z"),
            SourceBlockNumber: 42);
        var expected = new ArcSubscriptionSyncResult(
            new ArcSubscriptionMirror(
                request.WalletAddress,
                request.StrategyKey,
                "SignalViewer",
                [ArcEntitlementPermission.ViewSignals],
                request.ExpiresAtUtc,
                request.SourceTransactionHash,
                DateTimeOffset.Parse("2026-05-12T00:00:00Z"),
                request.SourceBlockNumber,
                request.PlanId),
            IsExpired: false,
            DateTimeOffset.Parse("2026-05-12T00:00:00Z"));
        var sync = new FakeArcSubscriptionSyncService { Result = expected };
        var controller = new ArcAccessController(
            new FakeArcSubscriptionPlanService([]),
            new FakeArcStrategyAccessReader(),
            sync);

        var result = await controller.Sync(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(expected, ok.Value);
        Assert.Same(request, sync.LastRequest);
    }

    [Fact]
    public async Task SyncMapsMissingPlanToNotFound()
    {
        var sync = new FakeArcSubscriptionSyncService
        {
            Exception = new ArcSubscriptionSyncException("PLAN_NOT_FOUND", "plan not found")
        };
        var controller = new ArcAccessController(
            new FakeArcSubscriptionPlanService([]),
            new FakeArcStrategyAccessReader(),
            sync);

        var result = await controller.Sync(
            new SyncArcAccessRequest(
                "0x1234567890abcdef1234567890abcdef12345678",
                "repricing_lag_arbitrage",
                999,
                "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                DateTimeOffset.Parse("2026-05-19T00:00:00Z")),
            CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    private sealed class FakeArcSubscriptionPlanService(
        IReadOnlyList<ArcSubscriptionPlan> plans) : IArcSubscriptionPlanService
    {
        public IReadOnlyList<ArcSubscriptionPlan> ListPlans()
            => plans;
    }

    private sealed class FakeArcStrategyAccessReader : IArcStrategyAccessReader
    {
        public string? LastWalletAddress { get; private set; }

        public string? LastStrategyKey { get; private set; }

        public ArcStrategyAccessStatus Result { get; init; } = new(
            string.Empty,
            string.Empty,
            ArcStrategyAccessStatusCode.NotFound,
            HasAccess: false,
            "ACCESS_NOT_FOUND",
            [],
            DateTimeOffset.Parse("2026-05-12T00:00:00Z"));

        public Task<ArcStrategyAccessStatus> GetAccessAsync(
            string walletAddress,
            string strategyKey,
            CancellationToken cancellationToken = default)
        {
            LastWalletAddress = walletAddress;
            LastStrategyKey = strategyKey;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeArcSubscriptionSyncService : IArcSubscriptionSyncService
    {
        public SyncArcAccessRequest? LastRequest { get; private set; }

        public ArcSubscriptionSyncResult? Result { get; init; }

        public ArcSubscriptionSyncException? Exception { get; init; }

        public Task<ArcSubscriptionSyncResult> SyncAsync(
            SyncArcAccessRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(
                Result
                ?? new ArcSubscriptionSyncResult(
                    new ArcSubscriptionMirror(
                        request.WalletAddress,
                        request.StrategyKey,
                        "SignalViewer",
                        [ArcEntitlementPermission.ViewSignals],
                        request.ExpiresAtUtc,
                        request.SourceTransactionHash,
                        DateTimeOffset.Parse("2026-05-12T00:00:00Z"),
                        request.SourceBlockNumber,
                        request.PlanId),
                    IsExpired: false,
                    DateTimeOffset.Parse("2026-05-12T00:00:00Z")));
        }
    }
}
