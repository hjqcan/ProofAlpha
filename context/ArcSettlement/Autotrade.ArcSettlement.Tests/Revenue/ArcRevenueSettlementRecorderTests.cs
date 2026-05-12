using System.Text.Json;
using Autotrade.ArcSettlement.Application.Configuration;
using Autotrade.ArcSettlement.Application.Contract.Configuration;
using Autotrade.ArcSettlement.Application.Contract.Revenue;
using Autotrade.ArcSettlement.Application.Revenue;
using Microsoft.Extensions.Options;

namespace Autotrade.ArcSettlement.Tests.Revenue;

public sealed class ArcRevenueSettlementRecorderTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly string SignalId = Hash32("signal-1");

    [Fact]
    public async Task RecordAsync_WhenArcDisabled_CreatesSkippedLocalJournalWithDeterministicSplit()
    {
        var store = new InMemoryRevenueSettlementStore();
        var publisher = new FakeRevenueSettlementPublisher();
        var recorder = CreateRecorder(
            store,
            publisher,
            new ArcSettlementOptions { Enabled = false });

        var result = await recorder.RecordAsync(CreateRequest(grossUsdc: 10m));

        Assert.False(result.AlreadyRecorded);
        Assert.Equal(ArcRevenueSettlementStatus.SkippedDisabled, result.Record.Status);
        Assert.Equal("ARC_DISABLED", result.Record.ErrorCode);
        Assert.Equal(10_000_000, result.Record.GrossMicroUsdc);
        Assert.Equal(10_000_000, result.Record.Shares.Sum(share => share.AmountMicroUsdc));
        Assert.Equal(7_000_000, result.Record.Shares[0].AmountMicroUsdc);
        Assert.Equal(2_000_000, result.Record.Shares[1].AmountMicroUsdc);
        Assert.Equal(1_000_000, result.Record.Shares[2].AmountMicroUsdc);
        Assert.Equal(0, publisher.CallCount);
        Assert.Single(await store.ListAsync(20));
    }

    [Fact]
    public async Task RecordAsync_WhenEnabled_PublishesSettlementAndStoresTransaction()
    {
        var store = new InMemoryRevenueSettlementStore();
        var publisher = new FakeRevenueSettlementPublisher("0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var recorder = CreateRecorder(
            store,
            publisher,
            CreateEnabledOptions(hasSecret: true));

        var result = await recorder.RecordAsync(CreateRequest(
            sourceKind: ArcRevenueSourceKind.SubscriptionFee,
            simulated: false,
            sourceTransactionHash: Hash32("subscription-tx"),
            grossUsdc: 25m));

        Assert.Equal(ArcRevenueSettlementStatus.Confirmed, result.Record.Status);
        Assert.Equal("0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", result.Record.TransactionHash);
        Assert.Equal("https://explorer.arc.test/tx/0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", result.Record.ExplorerUrl);
        Assert.Equal(1, publisher.CallCount);
        Assert.NotNull(publisher.LastPayload);
        Assert.Equal(result.Record.SettlementId, publisher.LastPayload.SettlementId);
        Assert.Equal(SignalId, publisher.LastPayload.SignalId);
        Assert.Equal("25000000", publisher.LastPayload.GrossAmountMicroUsdc);
        Assert.Equal([7000, 2000, 1000], publisher.LastPayload.ShareBps);
    }

    [Fact]
    public async Task RecordAsync_ManualDemoSettlementRequiresSimulatedFlag()
    {
        var recorder = CreateRecorder(
            new InMemoryRevenueSettlementStore(),
            new FakeRevenueSettlementPublisher(),
            new ArcSettlementOptions { Enabled = false });

        await Assert.ThrowsAsync<ArgumentException>(
            () => recorder.RecordAsync(CreateRequest(simulated: false)));
    }

    [Fact]
    public async Task RecordAsync_DuplicateSettlementIdReturnsExistingRecordWithoutRepublishing()
    {
        var store = new InMemoryRevenueSettlementStore();
        var publisher = new FakeRevenueSettlementPublisher();
        var recorder = CreateRecorder(
            store,
            publisher,
            CreateEnabledOptions(hasSecret: true));
        var request = CreateRequest(
            settlementId: Hash32("settlement-id"),
            sourceKind: ArcRevenueSourceKind.SubscriptionFee,
            simulated: false,
            sourceTransactionHash: Hash32("subscription-tx"));

        var first = await recorder.RecordAsync(request);
        var second = await recorder.RecordAsync(request);

        Assert.False(first.AlreadyRecorded);
        Assert.True(second.AlreadyRecorded);
        Assert.Equal(first.Record.SettlementId, second.Record.SettlementId);
        Assert.Equal(first.Record.TransactionHash, second.Record.TransactionHash);
        Assert.Equal(1, publisher.CallCount);
        Assert.Single(await store.ListAsync(20));
    }

    [Fact]
    public async Task RecordAsync_DistributesMicroUsdcRemainderWithoutDrift()
    {
        var recorder = CreateRecorder(
            new InMemoryRevenueSettlementStore(),
            new FakeRevenueSettlementPublisher(),
            new ArcSettlementOptions { Enabled = false });

        var result = await recorder.RecordAsync(CreateRequest(
            grossUsdc: 0.000001m,
            shares:
            [
                new ArcRevenueSplitShareRequest(
                    ArcRevenueRecipientKind.AgentOwner,
                    "0x1000000000000000000000000000000000000001",
                    3333),
                new ArcRevenueSplitShareRequest(
                    ArcRevenueRecipientKind.StrategyAuthor,
                    "0x2000000000000000000000000000000000000002",
                    3333),
                new ArcRevenueSplitShareRequest(
                    ArcRevenueRecipientKind.Platform,
                    "0x3000000000000000000000000000000000000003",
                    3334)
            ]));

        Assert.Equal(1, result.Record.GrossMicroUsdc);
        Assert.Equal(1, result.Record.Shares.Sum(share => share.AmountMicroUsdc));
        Assert.Equal(0, result.Record.Shares[0].AmountMicroUsdc);
        Assert.Equal(0, result.Record.Shares[1].AmountMicroUsdc);
        Assert.Equal(1, result.Record.Shares[2].AmountMicroUsdc);
    }

    [Fact]
    public async Task RecordAsync_ResponseDoesNotSerializeSecretMaterial()
    {
        var recorder = CreateRecorder(
            new InMemoryRevenueSettlementStore(),
            new FakeRevenueSettlementPublisher(),
            CreateEnabledOptions(hasSecret: true));

        var result = await recorder.RecordAsync(CreateRequest(
            sourceKind: ArcRevenueSourceKind.SubscriptionFee,
            simulated: false,
            sourceTransactionHash: Hash32("subscription-tx")));
        var json = JsonSerializer.Serialize(result);

        Assert.DoesNotContain("privateKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiSecret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ARC_SETTLEMENT_PRIVATE_KEY", json, StringComparison.Ordinal);
    }

    private static ArcRevenueSettlementRecorder CreateRecorder(
        IArcRevenueSettlementStore store,
        IArcRevenueSettlementPublisher publisher,
        ArcSettlementOptions options,
        bool hasSecret = true)
        => new(
            store,
            publisher,
            new ArcSettlementOptionsValidator(new StaticSecretSource(hasSecret)),
            new StaticOptionsMonitor<ArcSettlementOptions>(options),
            new FixedTimeProvider(Now));

    private static ArcSettlementOptions CreateEnabledOptions(bool hasSecret)
        => new()
        {
            Enabled = true,
            ChainId = 31337,
            RpcUrl = "http://127.0.0.1:8545",
            BlockExplorerBaseUrl = "https://explorer.arc.test",
            Contracts = new ArcSettlementContractsOptions
            {
                SignalRegistry = "0x1111111111111111111111111111111111111111",
                StrategyAccess = "0x2222222222222222222222222222222222222222",
                PerformanceLedger = "0x3333333333333333333333333333333333333333",
                RevenueSettlement = "0x4444444444444444444444444444444444444444"
            },
            Wallet = new ArcSettlementWalletOptions
            {
                PrivateKeyEnvironmentVariable = hasSecret ? "ARC_SETTLEMENT_PRIVATE_KEY" : "MISSING_PRIVATE_KEY"
            },
            SignalProof = new ArcSettlementSignalProofOptions
            {
                AgentAddress = "0x9999999999999999999999999999999999999999"
            }
        };

    private static ArcRevenueSettlementRequest CreateRequest(
        string? settlementId = null,
        ArcRevenueSourceKind sourceKind = ArcRevenueSourceKind.ManualDemoSettlement,
        bool simulated = true,
        string? sourceTransactionHash = null,
        decimal grossUsdc = 10m,
        IReadOnlyList<ArcRevenueSplitShareRequest>? shares = null)
        => new(
            settlementId,
            sourceKind,
            SignalId,
            "paper-order-1",
            "0x9000000000000000000000000000000000000009",
            "repricing_lag_arbitrage",
            grossUsdc,
            TokenAddress: null,
            shares,
            "phase 10 settlement fixture",
            simulated,
            sourceTransactionHash,
            CreatedAtUtc: Now.AddSeconds(-10));

    private static string Hash32(string value)
        => $"0x{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant()}";

    private sealed class InMemoryRevenueSettlementStore : IArcRevenueSettlementStore
    {
        private readonly List<ArcRevenueSettlementRecord> _records = [];

        public Task<ArcRevenueSettlementRecord?> GetBySettlementIdAsync(
            string settlementId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_records.FirstOrDefault(record => IsSame(record.SettlementId, settlementId)));

        public Task<IReadOnlyList<ArcRevenueSettlementRecord>> ListAsync(
            int limit,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ArcRevenueSettlementRecord>>(
                _records.OrderByDescending(record => record.RecordedAtUtc).Take(limit).ToArray());

        public Task UpsertAsync(
            ArcRevenueSettlementRecord record,
            CancellationToken cancellationToken = default)
        {
            var index = _records.FindIndex(item => IsSame(item.SettlementId, record.SettlementId));
            if (index >= 0)
            {
                _records[index] = record;
            }
            else
            {
                _records.Add(record);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeRevenueSettlementPublisher(
        string transactionHash = "0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb") : IArcRevenueSettlementPublisher
    {
        public int CallCount { get; private set; }

        public ArcRevenueSettlementPublishPayload? LastPayload { get; private set; }

        public Task<ArcRevenueSettlementPublishResult> PublishAsync(
            ArcRevenueSettlementPublishPayload payload,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastPayload = payload;
            return Task.FromResult(new ArcRevenueSettlementPublishResult(transactionHash, Confirmed: true));
        }
    }

    private sealed class StaticSecretSource(bool hasSecret) : IArcSettlementSecretSource
    {
        public bool HasSecret(string environmentVariableName)
            => hasSecret;
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

    private static bool IsSame(string left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
