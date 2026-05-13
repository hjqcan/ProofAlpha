using Autotrade.Application.DTOs;
using Autotrade.Domain.Abstractions.EventBus;
using Autotrade.OpportunityDiscovery.Application;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.OpportunityDiscovery.Domain.Entities;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using Autotrade.OpportunityDiscovery.Infra.Data.Context;
using Autotrade.OpportunityDiscovery.Infra.Data.Repositories;
using Autotrade.Trading.Application.Contract.Accounts;
using Autotrade.Trading.Application.Contract.Compliance;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetDevPack.Messaging;

namespace Autotrade.OpportunityDiscovery.Tests;

public sealed class OpportunityLiveAllocationServiceTests
{
    [Fact]
    public async Task TryCreateMicroAllocationAsync_BlocksWhenLiveArmingIsNotActive()
    {
        await using var context = CreateContext();
        var repository = new OpportunityV2Repository(context);
        var (hypothesis, policy) = await SeedLiveEligibleAsync(repository);
        var service = CreateService(
            context,
            liveArming: new FakeLiveArmingService(isArmed: false),
            accountSync: new FakeAccountSyncService(DateTimeOffset.UtcNow),
            riskManager: new FakeRiskManager(),
            complianceGuard: new FakeComplianceGuard());

        var result = await service.TryCreateMicroAllocationAsync(AllocationRequest(hypothesis.Id, policy.Id));

        Assert.False(result.Accepted);
        Assert.Contains(
            result.BlockingReasons,
            reason => reason.Contains("Live arming", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(context.OpportunityLiveAllocations);
        Assert.Equal(OpportunityHypothesisStatus.LiveEligible, context.OpportunityHypotheses.Single().Status);
    }

    [Fact]
    public async Task TryCreateMicroAllocationAsync_AllControlsPassCreatesMicroAllocationAndPublishesLive()
    {
        await using var context = CreateContext();
        var repository = new OpportunityV2Repository(context);
        var (hypothesis, policy) = await SeedLiveEligibleAsync(repository);
        var service = CreateService(
            context,
            liveArming: new FakeLiveArmingService(isArmed: true),
            accountSync: new FakeAccountSyncService(DateTimeOffset.UtcNow),
            riskManager: new FakeRiskManager(),
            complianceGuard: new FakeComplianceGuard());

        var result = await service.TryCreateMicroAllocationAsync(AllocationRequest(hypothesis.Id, policy.Id));

        Assert.True(result.Accepted);
        Assert.NotNull(result.Allocation);
        Assert.Equal(10m, result.Allocation.MaxNotional);
        Assert.Equal(OpportunityLiveAllocationStatus.Active, result.Allocation.Status);
        Assert.Equal(OpportunityHypothesisStatus.LivePublished, result.Hypothesis!.Status);
        var persisted = await context.OpportunityHypotheses.SingleAsync(item => item.Id == hypothesis.Id);
        Assert.Equal(OpportunityHypothesisStatus.LivePublished, persisted.Status);
        Assert.Equal(result.Allocation.Id, persisted.ActiveLiveAllocationId);
        Assert.Single(context.OpportunityLiveAllocations);
        Assert.Single(context.OpportunityLifecycleTransitions);
    }

    [Fact]
    public async Task TryCreateMicroAllocationAsync_IncreasedLimitsRequireComplianceUnsafeSupport()
    {
        await using var context = CreateContext();
        var repository = new OpportunityV2Repository(context);
        var (hypothesis, policy) = await SeedLiveEligibleAsync(repository);
        var service = CreateService(
            context,
            liveArming: new FakeLiveArmingService(isArmed: true),
            accountSync: new FakeAccountSyncService(DateTimeOffset.UtcNow),
            riskManager: new FakeRiskManager(),
            complianceGuard: new FakeComplianceGuard(),
            options: new OpportunityLiveAllocationOptions
            {
                SingleOpportunityMaxNotionalUsdc = 11m
            },
            complianceOptions: new ComplianceOptions { AllowUnsafeLiveParameters = false });

        var result = await service.TryCreateMicroAllocationAsync(AllocationRequest(hypothesis.Id, policy.Id));

        Assert.False(result.Accepted);
        Assert.Contains(
            result.BlockingReasons,
            reason => reason.Contains("increasing default Live allocation limits", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SuspendIfKillCriteriaAsync_SuspendsPolicyAllocationHypothesisAndCancelsOpenOrders()
    {
        await using var context = CreateContext();
        var repository = new OpportunityV2Repository(context);
        var (hypothesis, policy, allocation) = await SeedLivePublishedAsync(repository);
        var orderRepository = new FakeOrderRepository([
            OpenOrder("keep-other-market", "other-market", "llm-opportunity"),
            OpenOrder("cancel-me", policy.MarketId, "llm-opportunity")
        ]);
        var execution = new FakeExecutionService();
        var service = CreateService(
            context,
            liveArming: new FakeLiveArmingService(isArmed: true),
            accountSync: new FakeAccountSyncService(DateTimeOffset.UtcNow),
            riskManager: new FakeRiskManager(),
            complianceGuard: new FakeComplianceGuard(),
            orderRepository: orderRepository,
            executionService: execution);

        var result = await service.SuspendIfKillCriteriaAsync(new OpportunitySuspensionRequest(
            hypothesis.Id,
            policy.Id,
            allocation.Id,
            "llm-opportunity",
            null,
            "risk-monitor",
            "kill criteria",
            KillMetrics(riskEventCount: 1),
            [Guid.NewGuid()]));

        Assert.True(result.Suspended);
        Assert.Contains(result.KillReasons, reason => reason.Contains("risk event count", StringComparison.OrdinalIgnoreCase));
        var persistedHypothesis = await context.OpportunityHypotheses.SingleAsync(item => item.Id == hypothesis.Id);
        var persistedPolicy = await context.ExecutableOpportunityPolicies.SingleAsync(item => item.Id == policy.Id);
        var persistedAllocation = await context.OpportunityLiveAllocations.SingleAsync(item => item.Id == allocation.Id);
        Assert.Equal(OpportunityHypothesisStatus.Suspended, persistedHypothesis.Status);
        Assert.Equal(ExecutableOpportunityPolicyStatus.Suspended, persistedPolicy.Status);
        Assert.Equal(OpportunityLiveAllocationStatus.Suspended, persistedAllocation.Status);
        var canceled = Assert.Single(result.CanceledOrders);
        Assert.Equal("cancel-me", canceled.ClientOrderId);
        Assert.True(canceled.Accepted);
        Assert.Equal(["cancel-me"], execution.CanceledClientOrderIds);

        var feed = new ExecutableOpportunityPolicyFeed(repository);
        Assert.Empty(await feed.GetExecutableAsync());
        Assert.Single(context.OpportunityLifecycleTransitions);
    }

    private static OpportunityLiveAllocationRequest AllocationRequest(Guid hypothesisId, Guid policyId)
        => new(
            hypothesisId,
            policyId,
            RequestedMaxNotional: 10m,
            RequestedMaxContracts: 10m,
            DateTimeOffset.UtcNow.AddHours(1),
            "test",
            "micro allocation",
            [Guid.NewGuid()]);

    private static OpportunityKillCriteriaMetrics KillMetrics(int riskEventCount = 0)
        => new(
            RealizedEdge: 0.02m,
            PredictedEdge: 0.10m,
            AdverseSlippage: 0.01m,
            FillRate: 0.80m,
            DrawdownUsdc: 0m,
            SourceDriftScore: 0m,
            CalibrationDriftScore: 0m,
            OrderBookAgeSeconds: 1,
            RiskEventCount: riskEventCount,
            ComplianceEventCount: 0);

    private static async Task<(OpportunityHypothesis Hypothesis, ExecutableOpportunityPolicy Policy)> SeedLiveEligibleAsync(
        OpportunityV2Repository repository)
    {
        var now = DateTimeOffset.UtcNow;
        var hypothesis = PaperValidatedHypothesis();
        var policy = Policy(hypothesis.Id, now);
        policy.Activate(now);
        var gates = RequiredGates(hypothesis.Id, now);
        hypothesis.MarkLiveEligible(gates, policy.Id, "test", "gates passed", [], now.AddSeconds(1));
        await repository.AddHypothesisAsync(hypothesis);
        await repository.AddExecutablePolicyAsync(policy);
        foreach (var gate in gates)
        {
            await repository.AddPromotionGateAsync(gate);
        }

        return (hypothesis, policy);
    }

    private static async Task<(OpportunityHypothesis Hypothesis, ExecutableOpportunityPolicy Policy, OpportunityLiveAllocation Allocation)> SeedLivePublishedAsync(
        OpportunityV2Repository repository)
    {
        var now = DateTimeOffset.UtcNow;
        var (hypothesis, policy) = await SeedLiveEligibleAsync(repository);
        var allocation = new OpportunityLiveAllocation(
            hypothesis.Id,
            policy.Id,
            10m,
            10m,
            now.AddHours(1),
            "existing allocation",
            now);
        hypothesis.PublishLive(
            RequiredGates(hypothesis.Id, now),
            policy.Id,
            allocation.Id,
            "test",
            "published",
            [],
            now.AddSeconds(2));
        await repository.UpdateHypothesisAsync(hypothesis);
        await repository.AddLiveAllocationAsync(allocation);
        return (hypothesis, policy, allocation);
    }

    private static OpportunityHypothesis PaperValidatedHypothesis()
    {
        var now = DateTimeOffset.UtcNow;
        var hypothesis = new OpportunityHypothesis(
            Guid.NewGuid(),
            "market-1",
            OpportunityOutcomeSide.Yes,
            Guid.NewGuid(),
            "tape:market-1",
            "prompt-v1",
            "model-v1",
            "paper validated",
            now);
        hypothesis.MarkScored("score-v1", "test", "scored", [], now.AddSeconds(1));
        var backtestGate = new OpportunityPromotionGate(
            hypothesis.Id,
            OpportunityPromotionGateKind.Backtest,
            OpportunityPromotionGateStatus.Passed,
            "test",
            "backtest",
            "{}",
            "[]",
            now.AddSeconds(2));
        hypothesis.MarkBacktestPassed("replay-seed", [backtestGate], "test", "backtest", [], now.AddSeconds(3));
        var paperGate = new OpportunityPromotionGate(
            hypothesis.Id,
            OpportunityPromotionGateKind.Paper,
            OpportunityPromotionGateStatus.Passed,
            "test",
            "paper",
            "{}",
            "[]",
            now.AddSeconds(4));
        hypothesis.MarkPaperValidated([paperGate], "test", "paper", [], now.AddSeconds(5));
        return hypothesis;
    }

    private static IReadOnlyList<OpportunityPromotionGate> RequiredGates(Guid hypothesisId, DateTimeOffset now)
        => OpportunityHypothesis.RequiredLiveGateKinds
            .Select((kind, index) => new OpportunityPromotionGate(
                hypothesisId,
                kind,
                OpportunityPromotionGateStatus.Passed,
                "test",
                $"{kind} gate",
                "{}",
                "[]",
                now.AddSeconds(index)))
            .ToArray();

    private static ExecutableOpportunityPolicy Policy(Guid hypothesisId, DateTimeOffset now)
        => new(
            hypothesisId,
            "policy-v1",
            "market-1",
            OpportunityOutcomeSide.Yes,
            0.65m,
            0.70m,
            0.08m,
            0.55m,
            0.70m,
            0.45m,
            0.02m,
            10m,
            5m,
            now.AddMinutes(-1),
            now.AddHours(1),
            "[]",
            now);

    private static OrderDto OpenOrder(string clientOrderId, string marketId, string strategyId)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            marketId,
            "token-1",
            strategyId,
            clientOrderId,
            "exchange-" + clientOrderId,
            null,
            OutcomeSide.Yes,
            OrderSide.Buy,
            OrderType.Limit,
            TimeInForce.Gtc,
            null,
            false,
            0.55m,
            10m,
            0m,
            OrderStatus.Open,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

    private static OpportunityLiveAllocationService CreateService(
        OpportunityDiscoveryContext context,
        ILiveArmingService? liveArming = null,
        IAccountSyncService? accountSync = null,
        IRiskManager? riskManager = null,
        IComplianceGuard? complianceGuard = null,
        IOrderRepository? orderRepository = null,
        IExecutionService? executionService = null,
        OpportunityLiveAllocationOptions? options = null,
        ComplianceOptions? complianceOptions = null)
        => new(
            new OpportunityV2Repository(context),
            liveArming is null ? [] : [liveArming],
            accountSync is null ? [] : [accountSync],
            riskManager is null ? [] : [riskManager],
            complianceGuard is null ? [] : [complianceGuard],
            orderRepository is null ? [] : [orderRepository],
            executionService is null ? [] : [executionService],
            Options.Create(options ?? new OpportunityLiveAllocationOptions()),
            Options.Create(complianceOptions ?? new ComplianceOptions()));

    private static OpportunityDiscoveryContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OpportunityDiscoveryContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new OpportunityDiscoveryContext(
            options,
            new NoopDomainEventDispatcher(),
            NullLogger<OpportunityDiscoveryContext>.Instance);
    }

    private sealed class FakeLiveArmingService(bool isArmed) : ILiveArmingService
    {
        public Task<LiveArmingStatus> GetStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Status());

        public Task<LiveArmingResult> ArmAsync(
            LiveArmingRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new LiveArmingResult(isArmed, isArmed ? "Accepted" : "Blocked", "test", Status()));

        public Task<LiveArmingResult> DisarmAsync(
            LiveDisarmingRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new LiveArmingResult(true, "Accepted", "test", Status()));

        public Task<LiveArmingStatus> RequireArmedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Status());

        private LiveArmingStatus Status()
            => new(
                isArmed,
                isArmed ? "Armed" : "Blocked",
                isArmed ? "armed" : "not armed",
                "test",
                DateTimeOffset.UtcNow,
                null,
                isArmed ? [] : ["not armed"]);
    }

    private sealed class FakeAccountSyncService(DateTimeOffset? lastSyncTime) : IAccountSyncService
    {
        public DateTimeOffset? LastSyncTime { get; } = lastSyncTime;

        public ExternalBalanceSnapshot? LastBalanceSnapshot => null;

        public IReadOnlyList<ExternalPositionSnapshot>? LastPositionsSnapshot => null;

        public Task<BalanceSyncResult> SyncBalanceAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PositionsSyncResult> SyncPositionsAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OpenOrdersSyncResult> SyncOpenOrdersAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<FullSyncResult> SyncAllAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeRiskManager : IRiskManager
    {
        public bool IsKillSwitchActive => false;

        public Task<RiskCheckResult> ValidateOrderAsync(RiskOrderRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task RecordOrderAcceptedAsync(RiskOrderRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task RecordOrderUpdateAsync(RiskOrderUpdate update, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task RecordOrderErrorAsync(string strategyId, string clientOrderId, string errorCode, string message, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ActivateKillSwitchAsync(string reason, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ActivateKillSwitchAsync(KillSwitchLevel level, string reasonCode, string reason, string? contextJson = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ActivateStrategyKillSwitchAsync(string strategyId, KillSwitchLevel level, string reasonCode, string reason, string? marketId = null, string? contextJson = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ResetKillSwitchAsync(string? strategyId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public KillSwitchState GetKillSwitchState() => KillSwitchState.Inactive;

        public KillSwitchState GetStrategyKillSwitchState(string strategyId) => KillSwitchState.Inactive;

        public bool IsStrategyBlocked(string strategyId) => false;

        public IReadOnlyList<KillSwitchState> GetAllActiveKillSwitches() => [];

        public IReadOnlyList<string> GetOpenOrderIds() => [];

        public IReadOnlyList<string> GetOpenOrderIds(string strategyId) => [];

        public IReadOnlyList<UnhedgedExposureSnapshot> GetExpiredUnhedgedExposures(DateTimeOffset nowUtc) => [];

        public Task RecordUnhedgedExposureAsync(
            string strategyId,
            string marketId,
            string tokenId,
            string hedgeTokenId,
            OutcomeSide outcome,
            OrderSide side,
            decimal quantity,
            decimal price,
            DateTimeOffset startedAtUtc,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ClearUnhedgedExposureAsync(string strategyId, string marketId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public RiskStateSnapshot GetStateSnapshot()
            => new(
                TotalOpenNotional: 0m,
                TotalOpenOrders: 0,
                TotalCapital: 1000m,
                AvailableCapital: 1000m,
                CapitalUtilizationPct: 0m,
                NotionalByStrategy: new Dictionary<string, decimal>(),
                NotionalByMarket: new Dictionary<string, decimal>(),
                OpenOrdersByStrategy: new Dictionary<string, int>(),
                UnhedgedExposures: []);
    }

    private sealed class FakeComplianceGuard : IComplianceGuard
    {
        public ComplianceCheckResult CheckConfiguration(ExecutionMode executionMode)
            => new(true, true, false, []);

        public ComplianceCheckResult CheckOrderPlacement(ExecutionMode executionMode)
            => new(true, true, false, []);
    }

    private sealed class FakeOrderRepository(IReadOnlyList<OrderDto> openOrders) : IOrderRepository
    {
        public Task AddAsync(OrderDto order, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task AddRangeAsync(IEnumerable<OrderDto> orders, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task UpdateAsync(OrderDto order, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<OrderDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<OrderDto?> GetByClientOrderIdAsync(string clientOrderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<OrderDto?> GetByExchangeOrderIdAsync(string exchangeOrderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<OrderDto>> GetOpenOrdersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(openOrders);

        public Task<IReadOnlyList<OrderDto>> GetByStrategyIdAsync(string strategyId, DateTimeOffset? from = null, DateTimeOffset? to = null, int? limit = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<OrderDto>> GetByMarketIdAsync(string marketId, DateTimeOffset? from = null, DateTimeOffset? to = null, int? limit = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<OrderDto>> GetByStatusAsync(OrderStatus status, DateTimeOffset? from = null, DateTimeOffset? to = null, int? limit = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PagedResultDto<OrderDto>> GetPagedAsync(int page, int pageSize, string? strategyId = null, string? marketId = null, OrderStatus? status = null, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> DeleteBeforeAsync(DateTimeOffset beforeUtc, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeExecutionService : IExecutionService
    {
        public List<string> CanceledClientOrderIds { get; } = [];

        public Task<ExecutionResult> PlaceOrderAsync(ExecutionRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ExecutionResult>> PlaceOrdersAsync(IReadOnlyList<ExecutionRequest> requests, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ExecutionResult> CancelOrderAsync(string clientOrderId, CancellationToken cancellationToken = default)
        {
            CanceledClientOrderIds.Add(clientOrderId);
            return Task.FromResult(ExecutionResult.Succeed(clientOrderId, "exchange-" + clientOrderId, ExecutionStatus.Cancelled));
        }

        public Task<OrderStatusResult> GetOrderStatusAsync(string clientOrderId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class NoopDomainEventDispatcher : IDomainEventDispatcher
    {
        public void Dispatch(IEnumerable<DomainEvent> domainEvents) { }

        public Task DispatchAsync(IEnumerable<DomainEvent> domainEvents) => Task.CompletedTask;
    }
}
