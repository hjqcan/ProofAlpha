using Autotrade.ArcSettlement.Application.Configuration;
using Autotrade.ArcSettlement.Application.Contract.Configuration;
using Autotrade.ArcSettlement.Application.Contract.Performance;
using Autotrade.ArcSettlement.Application.Contract.Proofs;
using Autotrade.ArcSettlement.Application.Contract.Signals;
using Autotrade.ArcSettlement.Application.Performance;
using Autotrade.ArcSettlement.Application.Signals;
using Microsoft.Extensions.Options;

namespace Autotrade.ArcSettlement.Tests.Performance;

public sealed class ArcPerformanceRecorderTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly string SignalId = Hash32("signal-1");

    [Fact]
    public async Task RecordAsync_WhenArcDisabled_CreatesSkippedLocalRecordWithoutPublisherCall()
    {
        var outcomeStore = new InMemoryPerformanceOutcomeStore();
        var publisher = new FakePerformanceLedgerPublisher();
        var recorder = CreateRecorder(
            outcomeStore,
            new InMemorySignalPublicationStore(),
            publisher,
            new ArcSettlementOptions { Enabled = false });

        var result = await recorder.RecordAsync(CreateRequest());

        Assert.False(result.AlreadyRecorded);
        Assert.Equal(ArcPerformanceRecordStatus.SkippedDisabled, result.Record.RecordStatus);
        Assert.Equal("ARC_DISABLED", result.Record.ErrorCode);
        Assert.Equal(0, publisher.CallCount);
        Assert.Single(await outcomeStore.ListAsync(20));
    }

    [Fact]
    public async Task RecordAsync_WhenEnabled_PublishesOutcomeStoresTransactionAndUpdatesReputation()
    {
        var signalStore = new InMemorySignalPublicationStore();
        await signalStore.UpsertAsync(CreateSignal(SignalId, validUntilUtc: Now.AddMinutes(30)));
        var outcomeStore = new InMemoryPerformanceOutcomeStore();
        var publisher = new FakePerformanceLedgerPublisher("0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var recorder = CreateRecorder(
            outcomeStore,
            signalStore,
            publisher,
            CreateEnabledOptions(hasSecret: true));

        var result = await recorder.RecordAsync(CreateRequest(
            status: ArcPerformanceOutcomeStatus.ExecutedWin,
            realizedPnlBps: 18m,
            slippageBps: -2m));
        var reputation = await recorder.GetAgentReputationAsync();

        Assert.Equal(ArcPerformanceRecordStatus.Confirmed, result.Record.RecordStatus);
        Assert.Equal("0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", result.Record.TransactionHash);
        Assert.Equal("https://explorer.arc.test/tx/0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", result.Record.ExplorerUrl);
        Assert.Equal(1, publisher.CallCount);
        Assert.NotNull(publisher.LastPayload);
        Assert.Equal(ArcPerformanceLedgerOutcomeStatus.Executed, publisher.LastPayload.Status);
        Assert.Equal(1, reputation.TotalSignals);
        Assert.Equal(1, reputation.TerminalSignals);
        Assert.Equal(0, reputation.PendingSignals);
        Assert.Equal(1, reputation.ExecutedSignals);
        Assert.Equal(1, reputation.WinCount);
        Assert.Equal(18m, reputation.AverageRealizedPnlBps);
        Assert.Equal(-2m, reputation.AverageSlippageBps);
        Assert.Equal(1m, reputation.ConfidenceCoverage);
    }

    [Fact]
    public async Task GetAgentReputationAsync_IncludesLossExpiredRejectedAndPendingWithoutInflatingWins()
    {
        var signalStore = new InMemorySignalPublicationStore();
        var lossSignalId = Hash32("loss");
        var rejectedSignalId = Hash32("rejected");
        var expiredSignalId = Hash32("expired");
        var pendingSignalId = Hash32("pending");
        await signalStore.UpsertAsync(CreateSignal(lossSignalId, strategyId: "dual_leg_arbitrage", validUntilUtc: Now.AddMinutes(30)));
        await signalStore.UpsertAsync(CreateSignal(rejectedSignalId, strategyId: "dual_leg_arbitrage", validUntilUtc: Now.AddMinutes(30)));
        await signalStore.UpsertAsync(CreateSignal(expiredSignalId, strategyId: "dual_leg_arbitrage", validUntilUtc: Now.AddMinutes(-1)));
        await signalStore.UpsertAsync(CreateSignal(pendingSignalId, strategyId: "dual_leg_arbitrage", validUntilUtc: Now.AddMinutes(30)));
        await signalStore.UpsertAsync(CreateSignal(Hash32("other-strategy"), strategyId: "other_strategy", validUntilUtc: Now.AddMinutes(30)));

        var outcomeStore = new InMemoryPerformanceOutcomeStore();
        await outcomeStore.UpsertAsync(CreateOutcome(lossSignalId, ArcPerformanceOutcomeStatus.ExecutedLoss, realizedPnlBps: -24m, slippageBps: 3m));
        await outcomeStore.UpsertAsync(CreateOutcome(rejectedSignalId, ArcPerformanceOutcomeStatus.RejectedRisk, realizedPnlBps: null, slippageBps: null));
        var recorder = CreateRecorder(
            outcomeStore,
            signalStore,
            new FakePerformanceLedgerPublisher(),
            new ArcSettlementOptions { Enabled = false });

        var reputation = await recorder.GetStrategyReputationAsync("dual_leg_arbitrage");

        Assert.Equal("strategy", reputation.Scope);
        Assert.Equal("dual_leg_arbitrage", reputation.StrategyId);
        Assert.Equal(4, reputation.TotalSignals);
        Assert.Equal(3, reputation.TerminalSignals);
        Assert.Equal(1, reputation.PendingSignals);
        Assert.Equal(1, reputation.ExecutedSignals);
        Assert.Equal(1, reputation.ExpiredSignals);
        Assert.Equal(1, reputation.RejectedSignals);
        Assert.Equal(0, reputation.WinCount);
        Assert.Equal(1, reputation.LossCount);
        Assert.Equal(-24m, reputation.AverageRealizedPnlBps);
        Assert.Equal(3m, reputation.AverageSlippageBps);
        Assert.Equal(Decimal.Divide(1m, 3m), reputation.RiskRejectionRate);
        Assert.Equal(0.75m, reputation.ConfidenceCoverage);
    }

    [Fact]
    public async Task RecordAsync_WhenOutcomeAlreadyStored_ReturnsDuplicateWithoutRepublishing()
    {
        var signalStore = new InMemorySignalPublicationStore();
        await signalStore.UpsertAsync(CreateSignal(SignalId, validUntilUtc: Now.AddMinutes(30)));
        var outcomeStore = new InMemoryPerformanceOutcomeStore();
        var publisher = new FakePerformanceLedgerPublisher();
        var recorder = CreateRecorder(
            outcomeStore,
            signalStore,
            publisher,
            CreateEnabledOptions(hasSecret: true));

        var first = await recorder.RecordAsync(CreateRequest());
        var second = await recorder.RecordAsync(CreateRequest());

        Assert.False(first.AlreadyRecorded);
        Assert.True(second.AlreadyRecorded);
        Assert.Equal(ArcPerformanceRecordStatus.Duplicate, second.Record.RecordStatus);
        Assert.Equal("OUTCOME_ALREADY_RECORDED", second.Record.ErrorCode);
        Assert.Equal(1, publisher.CallCount);
        Assert.Single(await outcomeStore.ListAsync(20));
    }

    [Fact]
    public async Task RecordAsync_WhenLedgerReportsDuplicate_StoresDuplicateStatus()
    {
        var outcomeStore = new InMemoryPerformanceOutcomeStore();
        var publisher = new FakePerformanceLedgerPublisher(throwDuplicate: true);
        var recorder = CreateRecorder(
            outcomeStore,
            new InMemorySignalPublicationStore(),
            publisher,
            CreateEnabledOptions(hasSecret: true));

        var result = await recorder.RecordAsync(CreateRequest());

        Assert.True(result.AlreadyRecorded);
        Assert.Equal(ArcPerformanceRecordStatus.Duplicate, result.Record.RecordStatus);
        Assert.Equal("OUTCOME_ALREADY_RECORDED", result.Record.ErrorCode);
        Assert.Equal(1, publisher.CallCount);
    }

    [Theory]
    [InlineData(ArcPerformanceOutcomeStatus.ExecutedWin, -1)]
    [InlineData(ArcPerformanceOutcomeStatus.ExecutedLoss, 1)]
    [InlineData(ArcPerformanceOutcomeStatus.ExecutedFlat, 1)]
    public async Task RecordAsync_RejectsExecutedStatusesWithInvalidPnl(
        ArcPerformanceOutcomeStatus status,
        decimal realizedPnlBps)
    {
        var recorder = CreateRecorder(
            new InMemoryPerformanceOutcomeStore(),
            new InMemorySignalPublicationStore(),
            new FakePerformanceLedgerPublisher(),
            new ArcSettlementOptions { Enabled = false });

        await Assert.ThrowsAsync<ArgumentException>(
            () => recorder.RecordAsync(CreateRequest(status: status, realizedPnlBps: realizedPnlBps)));
    }

    [Fact]
    public async Task RecordAsync_RejectsFillRateOutsideZeroToOne()
    {
        var recorder = CreateRecorder(
            new InMemoryPerformanceOutcomeStore(),
            new InMemorySignalPublicationStore(),
            new FakePerformanceLedgerPublisher(),
            new ArcSettlementOptions { Enabled = false });

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => recorder.RecordAsync(CreateRequest(fillRate: 1.01m)));
    }

    private static ArcPerformanceRecorder CreateRecorder(
        IArcPerformanceOutcomeStore outcomeStore,
        IArcSignalPublicationStore signalStore,
        IArcPerformanceLedgerPublisher publisher,
        ArcSettlementOptions options,
        bool hasSecret = true)
        => new(
            outcomeStore,
            signalStore,
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

    private static RecordArcPerformanceOutcomeRequest CreateRequest(
        string? signalId = null,
        ArcPerformanceOutcomeStatus status = ArcPerformanceOutcomeStatus.ExecutedWin,
        decimal? realizedPnlBps = 12m,
        decimal? slippageBps = 1m,
        decimal? fillRate = 1m)
        => new(
            signalId ?? SignalId,
            "paper-order-1",
            "dual_leg_arbitrage",
            "market-1",
            status,
            realizedPnlBps,
            slippageBps,
            fillRate,
            ReasonCode: null,
            CreatedAtUtc: Now.AddSeconds(-10));

    private static ArcSignalPublicationRecord CreateSignal(
        string signalId,
        string strategyId = "dual_leg_arbitrage",
        DateTimeOffset? validUntilUtc = null)
        => new(
            signalId,
            ArcProofSourceKind.Opportunity,
            $"source-{signalId[^8..]}",
            "0x9999999999999999999999999999999999999999",
            strategyId,
            "market-1",
            "polymarket",
            Hash32($"reasoning-{signalId}"),
            Hash32($"risk-{signalId}"),
            42m,
            100m,
            validUntilUtc ?? Now.AddMinutes(30),
            ArcSignalPublicationStatus.Confirmed,
            Hash32($"signal-hash-{signalId}"),
            SourcePolicyHash: null,
            TransactionHash: Hash32($"tx-{signalId}"),
            ExplorerUrl: null,
            ErrorCode: null,
            CreatedAtUtc: Now.AddMinutes(-1),
            PublishedAtUtc: Now.AddSeconds(-30),
            Actor: "operator",
            Reason: "phase 7 reputation fixture");

    private static ArcPerformanceOutcomeRecord CreateOutcome(
        string signalId,
        ArcPerformanceOutcomeStatus status,
        decimal? realizedPnlBps,
        decimal? slippageBps)
        => new(
            Hash32($"outcome-{signalId}"),
            signalId,
            $"execution-{signalId[^6..]}",
            "dual_leg_arbitrage",
            "market-1",
            status,
            realizedPnlBps,
            slippageBps,
            FillRate: status is ArcPerformanceOutcomeStatus.ExecutedLoss ? 1m : null,
            ReasonCode: status is ArcPerformanceOutcomeStatus.RejectedRisk ? "RISK_LIMIT" : null,
            OutcomeHash: Hash32($"outcome-hash-{signalId}"),
            TransactionHash: Hash32($"outcome-tx-{signalId}"),
            ExplorerUrl: null,
            ArcPerformanceRecordStatus.Confirmed,
            ErrorCode: null,
            CreatedAtUtc: Now.AddSeconds(-20),
            RecordedAtUtc: Now.AddSeconds(-10));

    private static string Hash32(string value)
        => $"0x{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant()}";

    private sealed class InMemoryPerformanceOutcomeStore : IArcPerformanceOutcomeStore
    {
        private readonly List<ArcPerformanceOutcomeRecord> _records = [];

        public Task<ArcPerformanceOutcomeRecord?> GetBySignalIdAsync(
            string signalId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_records.FirstOrDefault(record => IsSame(record.SignalId, signalId)));

        public Task<IReadOnlyList<ArcPerformanceOutcomeRecord>> ListAsync(
            int limit,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ArcPerformanceOutcomeRecord>>(
                _records.OrderByDescending(record => record.RecordedAtUtc).Take(limit).ToArray());

        public Task UpsertAsync(
            ArcPerformanceOutcomeRecord record,
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
    }

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
    }

    private sealed class FakePerformanceLedgerPublisher(
        string transactionHash = "0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
        bool throwDuplicate = false) : IArcPerformanceLedgerPublisher
    {
        public int CallCount { get; private set; }

        public ArcPerformanceLedgerPublishPayload? LastPayload { get; private set; }

        public Task<ArcPerformanceLedgerPublishResult> PublishAsync(
            ArcPerformanceLedgerPublishPayload payload,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastPayload = payload;
            if (throwDuplicate)
            {
                throw new ArcPerformanceLedgerDuplicateException("duplicate outcome");
            }

            return Task.FromResult(new ArcPerformanceLedgerPublishResult(transactionHash, Confirmed: true));
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
