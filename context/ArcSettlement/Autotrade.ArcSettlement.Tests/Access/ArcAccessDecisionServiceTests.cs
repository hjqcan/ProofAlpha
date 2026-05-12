using Autotrade.ArcSettlement.Application.Access;
using Autotrade.ArcSettlement.Application.Contract.Access;

namespace Autotrade.ArcSettlement.Tests.Access;

public sealed class ArcAccessDecisionServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-12T00:00:00Z");
    private const string Wallet = "0x1234567890abcdef1234567890abcdef12345678";
    private const string StrategyKey = "repricing_lag_arbitrage";

    [Fact]
    public async Task ActiveEntitlementWithRequiredPermission_AllowsAccess()
    {
        var service = new ArcAccessDecisionService(
            new StubArcStrategyAccessReader(
                ActiveStatus([ArcEntitlementPermission.ViewSignals, ArcEntitlementPermission.ViewReasoning])));

        var decision = await service.EvaluateAsync(
            new ArcAccessDecisionRequest(
                Wallet,
                StrategyKey,
                ArcEntitlementPermission.ViewSignals,
                "arc-signal",
                "signal-1"));

        Assert.True(decision.Allowed);
        Assert.Equal("ACCESS_ALLOWED", decision.ReasonCode);
        Assert.Equal("SignalViewer", decision.Tier);
        Assert.Equal("0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", decision.EvidenceTransactionHash);
    }

    [Fact]
    public async Task ActiveEntitlementMissingRequiredPermission_DeniesAccess()
    {
        var service = new ArcAccessDecisionService(
            new StubArcStrategyAccessReader(ActiveStatus([ArcEntitlementPermission.ViewSignals])));

        var decision = await service.EvaluateAsync(
            new ArcAccessDecisionRequest(
                Wallet,
                StrategyKey,
                ArcEntitlementPermission.RequestPaperAutoTrade,
                "arc-autotrade",
                "repricing_lag_arbitrage"));

        Assert.False(decision.Allowed);
        Assert.Equal("MISSING_PERMISSION", decision.ReasonCode);
        Assert.Equal(ArcEntitlementPermission.RequestPaperAutoTrade, decision.RequiredPermission);
        Assert.Equal("0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", decision.EvidenceTransactionHash);
    }

    [Fact]
    public async Task ExpiredEntitlement_DeniesAccessAndPreservesEvidence()
    {
        var service = new ArcAccessDecisionService(
            new StubArcStrategyAccessReader(
                new ArcStrategyAccessStatus(
                    Wallet,
                    StrategyKey,
                    ArcStrategyAccessStatusCode.Expired,
                    HasAccess: false,
                    "ACCESS_EXPIRED",
                    [ArcEntitlementPermission.ViewSignals],
                    Now,
                    Tier: "SignalViewer",
                    ExpiresAtUtc: Now.AddSeconds(-1),
                    SourceTransactionHash: "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    SyncedAtUtc: Now)));

        var decision = await service.EvaluateAsync(
            new ArcAccessDecisionRequest(
                Wallet,
                StrategyKey,
                ArcEntitlementPermission.ViewSignals,
                "arc-signal",
                "signal-1"));

        Assert.False(decision.Allowed);
        Assert.Equal("ACCESS_EXPIRED", decision.ReasonCode);
        Assert.Equal(Now.AddSeconds(-1), decision.ExpiresAtUtc);
        Assert.Equal("0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", decision.EvidenceTransactionHash);
    }

    [Fact]
    public async Task MissingWallet_DeniesWithoutOwnershipClaim()
    {
        var reader = new StubArcStrategyAccessReader(
            new ArcStrategyAccessStatus(
                string.Empty,
                StrategyKey,
                ArcStrategyAccessStatusCode.MissingWallet,
                HasAccess: false,
                "MISSING_WALLET",
                [],
                Now));
        var service = new ArcAccessDecisionService(reader);

        var decision = await service.EvaluateAsync(
            new ArcAccessDecisionRequest(
                null,
                StrategyKey,
                ArcEntitlementPermission.ViewSignals,
                "arc-signal",
                "signal-1"));

        Assert.False(decision.Allowed);
        Assert.Equal("MISSING_WALLET", decision.ReasonCode);
        Assert.Contains("demo wallet", decision.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Null(decision.WalletAddress);
        Assert.Equal(string.Empty, reader.LastWalletAddress);
    }

    private static ArcStrategyAccessStatus ActiveStatus(IReadOnlyList<ArcEntitlementPermission> permissions)
        => new(
            Wallet,
            StrategyKey,
            ArcStrategyAccessStatusCode.Active,
            HasAccess: true,
            "ACCESS_ACTIVE",
            permissions,
            Now,
            Tier: "SignalViewer",
            ExpiresAtUtc: Now.AddDays(7),
            SourceTransactionHash: "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            SyncedAtUtc: Now);

    private sealed class StubArcStrategyAccessReader(
        ArcStrategyAccessStatus status) : IArcStrategyAccessReader
    {
        public string? LastWalletAddress { get; private set; }

        public Task<ArcStrategyAccessStatus> GetAccessAsync(
            string walletAddress,
            string strategyKey,
            CancellationToken cancellationToken = default)
        {
            LastWalletAddress = walletAddress;
            return Task.FromResult(status);
        }
    }
}
