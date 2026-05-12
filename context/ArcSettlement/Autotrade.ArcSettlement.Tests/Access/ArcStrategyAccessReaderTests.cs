using Autotrade.ArcSettlement.Application.Access;
using Autotrade.ArcSettlement.Application.Contract.Access;
using Autotrade.ArcSettlement.Application.Contract.Configuration;
using Microsoft.Extensions.Options;

namespace Autotrade.ArcSettlement.Tests.Access;

public sealed class ArcStrategyAccessReaderTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-12T00:00:00Z");
    private const string Wallet = "0x1234567890abcdef1234567890abcdef12345678";
    private const string StrategyKey = "repricing_lag_arbitrage";

    [Fact]
    public async Task DisabledArc_ReturnsExplicitDisabledStateWithoutReadingMirror()
    {
        var store = new InMemoryEntitlementMirrorStore();
        var reader = CreateReader(store, new ArcSettlementOptions { Enabled = false });

        var status = await reader.GetAccessAsync(Wallet, StrategyKey);

        Assert.Equal(ArcStrategyAccessStatusCode.Disabled, status.StatusCode);
        Assert.False(status.HasAccess);
        Assert.Equal("ARC_SETTLEMENT_DISABLED", status.Reason);
        Assert.Equal(0, store.GetCallCount);
    }

    [Theory]
    [InlineData("", ArcStrategyAccessStatusCode.MissingWallet, "MISSING_WALLET")]
    [InlineData("not-a-wallet", ArcStrategyAccessStatusCode.InvalidWallet, "INVALID_WALLET")]
    [InlineData("0x0000000000000000000000000000000000000000", ArcStrategyAccessStatusCode.InvalidWallet, "INVALID_WALLET")]
    public async Task InvalidWallet_IsDeniedWithReason(
        string walletAddress,
        ArcStrategyAccessStatusCode expectedStatus,
        string expectedReason)
    {
        var store = new InMemoryEntitlementMirrorStore();
        var reader = CreateReader(store, new ArcSettlementOptions { Enabled = true });

        var status = await reader.GetAccessAsync(walletAddress, StrategyKey);

        Assert.Equal(expectedStatus, status.StatusCode);
        Assert.False(status.HasAccess);
        Assert.Equal(expectedReason, status.Reason);
        Assert.Equal(0, store.GetCallCount);
    }

    [Fact]
    public async Task MissingMirror_IsDeniedWithReason()
    {
        var store = new InMemoryEntitlementMirrorStore();
        var reader = CreateReader(store, new ArcSettlementOptions { Enabled = true });

        var status = await reader.GetAccessAsync(Wallet, StrategyKey);

        Assert.Equal(ArcStrategyAccessStatusCode.NotFound, status.StatusCode);
        Assert.False(status.HasAccess);
        Assert.Equal("ACCESS_NOT_FOUND", status.Reason);
        Assert.Equal(1, store.GetCallCount);
    }

    [Fact]
    public async Task ActiveMirror_AllowsConfiguredPermissions()
    {
        var mirror = CreateMirror(Now.AddDays(1), [ArcEntitlementPermission.ViewSignals, ArcEntitlementPermission.ViewReasoning]);
        var store = new InMemoryEntitlementMirrorStore(mirror);
        var reader = CreateReader(store, new ArcSettlementOptions { Enabled = true });

        var status = await reader.GetAccessAsync("0x1234567890ABCDEF1234567890abcdef12345678", StrategyKey);

        Assert.Equal(ArcStrategyAccessStatusCode.Active, status.StatusCode);
        Assert.True(status.HasAccess);
        Assert.True(status.CanViewSignals);
        Assert.Equal("ACCESS_ACTIVE", status.Reason);
        Assert.Equal("0x1234567890abcdef1234567890abcdef12345678", status.WalletAddress);
        Assert.Equal(mirror.SourceTransactionHash, status.SourceTransactionHash);
        Assert.Equal(mirror.Permissions, status.Permissions);
    }

    [Fact]
    public async Task ExpiredMirror_DeniesAccessButPreservesAuditFields()
    {
        var mirror = CreateMirror(Now.AddSeconds(-1), [ArcEntitlementPermission.ViewSignals]);
        var store = new InMemoryEntitlementMirrorStore(mirror);
        var reader = CreateReader(store, new ArcSettlementOptions { Enabled = true });

        var status = await reader.GetAccessAsync(Wallet, StrategyKey);

        Assert.Equal(ArcStrategyAccessStatusCode.Expired, status.StatusCode);
        Assert.False(status.HasAccess);
        Assert.False(status.CanViewSignals);
        Assert.Equal("ACCESS_EXPIRED", status.Reason);
        Assert.Equal(mirror.ExpiresAtUtc, status.ExpiresAtUtc);
        Assert.Equal(mirror.SourceTransactionHash, status.SourceTransactionHash);
    }

    [Fact]
    public void SubscriptionPlanService_MapsConfiguredPlansAndPermissions()
    {
        var service = new ArcSubscriptionPlanService(
            new StaticOptionsMonitor<ArcSettlementOptions>(
                new ArcSettlementOptions
                {
                    SubscriptionPlans =
                    [
                        new ArcSettlementSubscriptionPlanOptions
                        {
                            PlanId = 2,
                            StrategyKey = StrategyKey,
                            PlanName = "Paper Autotrade",
                            Tier = "PaperAutotrade",
                            PriceUsdc = 25m,
                            DurationDays = 7,
                            Permissions = ["ViewSignals", "RequestPaperAutoTrade"],
                            MaxMarkets = 12,
                            AutoTradingAllowed = true,
                            LiveTradingAllowed = false,
                            CreatedAtUtc = Now
                        }
                    ]
                }),
            new FixedTimeProvider(Now));

        var plan = Assert.Single(service.ListPlans());

        Assert.Equal(2, plan.PlanId);
        Assert.Equal(604800, plan.DurationSeconds);
        Assert.Equal([ArcEntitlementPermission.ViewSignals, ArcEntitlementPermission.RequestPaperAutoTrade], plan.Permissions);
        Assert.True(plan.AutoTradingAllowed);
        Assert.False(plan.LiveTradingAllowed);
    }

    [Fact]
    public async Task SubscriptionSyncService_UpsertsMirrorFromConfiguredPlan()
    {
        var store = new InMemoryEntitlementMirrorStore();
        var service = CreateSyncService(store, CreatePlanOptions());

        var result = await service.SyncAsync(
            new SyncArcAccessRequest(
                "0x1234567890ABCDEF1234567890abcdef12345678",
                StrategyKey,
                1,
                "0xAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                Now.AddDays(7),
                SourceBlockNumber: 123));

        Assert.False(result.IsExpired);
        Assert.Equal("0x1234567890abcdef1234567890abcdef12345678", result.Mirror.WalletAddress);
        Assert.Equal("0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", result.Mirror.SourceTransactionHash);
        Assert.Equal("SignalViewer", result.Mirror.Tier);
        Assert.Equal([ArcEntitlementPermission.ViewSignals, ArcEntitlementPermission.ViewReasoning], result.Mirror.Permissions);

        var stored = await store.GetAsync(Wallet, StrategyKey);
        Assert.Equal(result.Mirror, stored);
    }

    [Fact]
    public async Task SubscriptionSyncService_RejectsInvalidTransactionHash()
    {
        var service = CreateSyncService(new InMemoryEntitlementMirrorStore(), CreatePlanOptions());

        var exception = await Assert.ThrowsAsync<ArcSubscriptionSyncException>(
            () => service.SyncAsync(
                new SyncArcAccessRequest(
                    Wallet,
                    StrategyKey,
                    1,
                    "not-a-tx",
                    Now.AddDays(7))));

        Assert.Equal("INVALID_TX_HASH", exception.ErrorCode);
    }

    [Fact]
    public async Task SubscriptionSyncService_RejectsPlanStrategyMismatch()
    {
        var service = CreateSyncService(new InMemoryEntitlementMirrorStore(), CreatePlanOptions());

        var exception = await Assert.ThrowsAsync<ArcSubscriptionSyncException>(
            () => service.SyncAsync(
                new SyncArcAccessRequest(
                    Wallet,
                    "different_strategy",
                    1,
                    "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    Now.AddDays(7))));

        Assert.Equal("PLAN_STRATEGY_MISMATCH", exception.ErrorCode);
    }

    private static ArcStrategyAccessReader CreateReader(
        IArcEntitlementMirrorStore store,
        ArcSettlementOptions options)
        => new(
            store,
            new StaticOptionsMonitor<ArcSettlementOptions>(options),
            new FixedTimeProvider(Now));

    private static ArcSubscriptionSyncService CreateSyncService(
        IArcEntitlementMirrorStore store,
        IReadOnlyList<ArcSettlementSubscriptionPlanOptions> plans)
        => new(
            store,
            new ArcSubscriptionPlanService(
                new StaticOptionsMonitor<ArcSettlementOptions>(
                    new ArcSettlementOptions { SubscriptionPlans = plans }),
                new FixedTimeProvider(Now)),
            new FixedTimeProvider(Now));

    private static IReadOnlyList<ArcSettlementSubscriptionPlanOptions> CreatePlanOptions()
        =>
        [
            new ArcSettlementSubscriptionPlanOptions
            {
                PlanId = 1,
                StrategyKey = StrategyKey,
                PlanName = "Signal Viewer",
                Tier = "SignalViewer",
                PriceUsdc = 10m,
                DurationDays = 7,
                Permissions = ["ViewSignals", "ViewReasoning"],
                MaxMarkets = 12,
                CreatedAtUtc = Now
            }
        ];

    private static ArcSubscriptionMirror CreateMirror(
        DateTimeOffset expiresAt,
        IReadOnlyList<ArcEntitlementPermission> permissions)
        => new(
            Wallet,
            StrategyKey,
            "SignalViewer",
            permissions,
            expiresAt,
            "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            Now,
            SourceBlockNumber: 42,
            PlanId: 1);

    private sealed class InMemoryEntitlementMirrorStore(
        params ArcSubscriptionMirror[] mirrors) : IArcEntitlementMirrorStore
    {
        private readonly List<ArcSubscriptionMirror> _mirrors = [.. mirrors];

        public int GetCallCount { get; private set; }

        public Task<ArcSubscriptionMirror?> GetAsync(
            string walletAddress,
            string strategyKey,
            CancellationToken cancellationToken = default)
        {
            GetCallCount++;
            return Task.FromResult(
                _mirrors.FirstOrDefault(mirror =>
                    string.Equals(mirror.WalletAddress, walletAddress, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(mirror.StrategyKey, strategyKey, StringComparison.OrdinalIgnoreCase)));
        }

        public Task<IReadOnlyList<ArcSubscriptionMirror>> ListAsync(
            int limit,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ArcSubscriptionMirror>>(_mirrors.Take(limit).ToArray());

        public Task UpsertAsync(
            ArcSubscriptionMirror mirror,
            CancellationToken cancellationToken = default)
        {
            _mirrors.RemoveAll(item =>
                string.Equals(item.WalletAddress, mirror.WalletAddress, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.StrategyKey, mirror.StrategyKey, StringComparison.OrdinalIgnoreCase));
            _mirrors.Add(mirror);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
            => now;
    }

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = currentValue;

        public T Get(string? name)
            => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener)
            => null;
    }
}
