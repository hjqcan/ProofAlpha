using Autotrade.ArcSettlement.Application.Configuration;
using Autotrade.ArcSettlement.Application.Contract.Configuration;
using Autotrade.ArcSettlement.Application.Contract.Proofs;
using Autotrade.ArcSettlement.Application.Contract.Signals;
using Autotrade.ArcSettlement.Application.Proofs;
using Autotrade.ArcSettlement.Application.Signals;
using Microsoft.Extensions.Options;

namespace Autotrade.ArcSettlement.Tests.Signals;

public sealed class ArcSignalPublicationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PublishAsync_WhenArcDisabled_CreatesSkippedLocalRecordWithoutPublisherCall()
    {
        var store = new InMemorySignalPublicationStore();
        var publisher = new FakeSignalRegistryPublisher();
        var service = CreateService(store, publisher, new ArcSettlementOptions { Enabled = false });

        var result = await service.PublishAsync(CreateRequest());

        Assert.False(result.AlreadyExisted);
        Assert.Equal(ArcSignalPublicationStatus.SkippedDisabled, result.Record.Status);
        Assert.Equal("ARC_DISABLED", result.Record.ErrorCode);
        Assert.Equal(Hash("provenance"), result.Record.ProvenanceHash);
        Assert.Equal("artifacts/arc-hackathon/demo-run/provenance/opportunity-phase8-1.json", result.Record.EvidenceUri);
        Assert.Equal(0, publisher.CallCount);
        Assert.Single(await store.ListAsync(20));
    }

    [Fact]
    public async Task PublishAsync_WhenSourceAlreadyPublished_ReturnsExistingRecordIdempotently()
    {
        var store = new InMemorySignalPublicationStore();
        var publisher = new FakeSignalRegistryPublisher();
        var service = CreateService(store, publisher, new ArcSettlementOptions { Enabled = false });
        var first = await service.PublishAsync(CreateRequest());

        var second = await service.PublishAsync(CreateRequest());

        Assert.True(second.AlreadyExisted);
        Assert.Equal(first.Record.SignalId, second.Record.SignalId);
        Assert.Single(await store.ListAsync(20));
    }

    [Fact]
    public async Task PublishAsync_RejectsExpiredSource()
    {
        var service = CreateService(
            new InMemorySignalPublicationStore(),
            new FakeSignalRegistryPublisher(),
            new ArcSettlementOptions { Enabled = false });

        var request = CreateRequest(signal: CreateSignal(validUntilUtc: Now.AddMinutes(-1)));

        var result = await service.PublishAsync(request);

        Assert.Equal(ArcSignalPublicationStatus.RejectedUnsafe, result.Record.Status);
        Assert.Equal("SOURCE_EXPIRED", result.Record.ErrorCode);
    }

    [Fact]
    public async Task PublishAsync_RejectsUnreviewedOpportunity()
    {
        var service = CreateService(
            new InMemorySignalPublicationStore(),
            new FakeSignalRegistryPublisher(),
            new ArcSettlementOptions { Enabled = false });

        var result = await service.PublishAsync(CreateRequest(
            sourceReviewStatus: ArcSignalSourceReviewStatus.Candidate));

        Assert.Equal(ArcSignalPublicationStatus.RejectedUnsafe, result.Record.Status);
        Assert.Equal("OPPORTUNITY_NOT_APPROVED", result.Record.ErrorCode);
    }

    [Fact]
    public async Task PublishAsync_WhenEnabled_PersistsTransactionHashAndExplorerUrl()
    {
        var publisher = new FakeSignalRegistryPublisher("0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var service = CreateService(
            new InMemorySignalPublicationStore(),
            publisher,
            CreateEnabledOptions(hasSecret: true));

        var result = await service.PublishAsync(CreateRequest());

        Assert.Equal(ArcSignalPublicationStatus.Confirmed, result.Record.Status);
        Assert.Equal("0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", result.Record.TransactionHash);
        Assert.Equal("https://explorer.arc.test/tx/0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", result.Record.ExplorerUrl);
        Assert.Equal(1, publisher.CallCount);
    }

    [Fact]
    public async Task PublishAsync_MapsContractDuplicateToDuplicateStatus()
    {
        var publisher = new FakeSignalRegistryPublisher(throwDuplicate: true);
        var service = CreateService(
            new InMemorySignalPublicationStore(),
            publisher,
            CreateEnabledOptions(hasSecret: true));

        var result = await service.PublishAsync(CreateRequest());

        Assert.Equal(ArcSignalPublicationStatus.Duplicate, result.Record.Status);
        Assert.Equal("ARC_SIGNAL_DUPLICATE", result.Record.ErrorCode);
    }

    [Fact]
    public async Task PublishFromSourceAsync_ResolvesSourceProofThenPublishes()
    {
        var resolver = new FakeSignalProofSourceResolver(
            new ArcSignalSourceProofResolution(
                CreateSignal() with
                {
                    SourceKind = ArcProofSourceKind.StrategyDecision,
                    SourceId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
                },
                ArcSignalSourceReviewStatus.Approved,
                Hash("policy")));
        var service = CreateService(
            new InMemorySignalPublicationStore(),
            new FakeSignalRegistryPublisher(),
            new ArcSettlementOptions { Enabled = false },
            sourceResolvers: [resolver]);

        var result = await service.PublishFromSourceAsync(
            new PublishArcSignalSourceRequest(
                ArcProofSourceKind.StrategyDecision,
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                "test-operator",
                "publish selected decision"));

        Assert.Equal(1, resolver.CallCount);
        Assert.Equal(ArcSignalPublicationStatus.SkippedDisabled, result.Record.Status);
        Assert.Equal(ArcProofSourceKind.StrategyDecision, result.Record.SourceKind);
    }

    private static ArcSignalPublicationService CreateService(
        IArcSignalPublicationStore store,
        IArcSignalRegistryPublisher publisher,
        ArcSettlementOptions options,
        bool hasSecret = true,
        IEnumerable<IArcSignalProofSourceResolver>? sourceResolvers = null)
        => new(
            store,
            publisher,
            new ArcProofHashService(),
            new ArcSettlementOptionsValidator(new StaticSecretSource(hasSecret)),
            new StaticOptionsMonitor<ArcSettlementOptions>(options),
            sourceResolvers ?? [],
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

    private static PublishArcSignalRequest CreateRequest(
        ArcStrategySignalProofDocument? signal = null,
        ArcSignalSourceReviewStatus sourceReviewStatus = ArcSignalSourceReviewStatus.Approved)
        => new(
            signal ?? CreateSignal(),
            sourceReviewStatus,
            "test-operator",
            "publish phase 3 signal proof",
            SourcePolicyHash: Hash("policy"));

    private static ArcStrategySignalProofDocument CreateSignal(DateTimeOffset? validUntilUtc = null)
        => new(
            "arc-strategy-signal-proof.v1",
            "0x9999999999999999999999999999999999999999",
            ArcProofSourceKind.Opportunity,
            "opportunity-1",
            "repricing_lag_arbitrage",
            "market-1",
            "polymarket",
            Now,
            "config-v1",
            ["ev-1", "ev-2"],
            Hash("opportunity"),
            Hash("reasoning"),
            Hash("risk"),
            42m,
            100m,
            validUntilUtc ?? Now.AddMinutes(15),
            GeneratedStrategyPackageHash: null,
            ProvenanceHash: Hash("provenance"),
            EvidenceUri: "artifacts/arc-hackathon/demo-run/provenance/opportunity-phase8-1.json");

    private static string Hash(string value)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed class InMemorySignalPublicationStore : IArcSignalPublicationStore
    {
        private readonly List<ArcSignalPublicationRecord> _records = [];

        public Task<ArcSignalPublicationRecord?> GetBySignalIdAsync(
            string signalId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_records.FirstOrDefault(record => IsSame(record.SignalId, signalId)));

        public Task<ArcSignalPublicationRecord?> GetBySourceAsync(
            ArcProofSourceKind sourceKind,
            string sourceId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_records.FirstOrDefault(record =>
                record.SourceKind == sourceKind && IsSame(record.SourceId, sourceId)));

        public Task<IReadOnlyList<ArcSignalPublicationRecord>> ListAsync(
            int limit,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ArcSignalPublicationRecord>>(
                _records.OrderByDescending(record => record.CreatedAtUtc).Take(limit).ToArray());

        public Task UpsertAsync(
            ArcSignalPublicationRecord record,
            CancellationToken cancellationToken = default)
        {
            var index = _records.FindIndex(item => IsSame(item.SignalId, record.SignalId));
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

        private static bool IsSame(string left, string right)
            => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeSignalRegistryPublisher(
        string transactionHash = "0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
        bool throwDuplicate = false) : IArcSignalRegistryPublisher
    {
        public int CallCount { get; private set; }

        public Task<ArcSignalRegistryPublishResult> PublishAsync(
            ArcSignalRegistryPublishPayload payload,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (throwDuplicate)
            {
                throw new ArcSignalRegistryDuplicateException("duplicate signal");
            }

            return Task.FromResult(new ArcSignalRegistryPublishResult(transactionHash, Confirmed: true));
        }
    }

    private sealed class FakeSignalProofSourceResolver(
        ArcSignalSourceProofResolution resolution) : IArcSignalProofSourceResolver
    {
        public ArcProofSourceKind SourceKind => resolution.SignalProof.SourceKind;

        public int CallCount { get; private set; }

        public Task<ArcSignalSourceProofResolution> ResolveAsync(
            PublishArcSignalSourceRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(resolution);
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
}
