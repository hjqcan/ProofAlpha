using System.Text.Json;
using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.OrderBook;
using Autotrade.MarketData.Domain.Shared.ValueObjects;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Strategies.Opportunity;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Options;

namespace Autotrade.Strategy.Tests;

public sealed class LlmOpportunityStrategyTests
{
    [Fact]
    public async Task SelectMarketsAsync_OnlyReturnsExecutableUnexpiredPolicies()
    {
        var strategy = CreateStrategy(
            [
                Policy("mkt-1", DateTimeOffset.UtcNow.AddHours(1)),
                Policy("mkt-expired", DateTimeOffset.UtcNow.AddMinutes(-1)),
                Policy("mkt-suspended", DateTimeOffset.UtcNow.AddHours(1), ExecutableOpportunityPolicyStatus.Suspended)
            ]);

        var markets = await strategy.SelectMarketsAsync();

        Assert.Equal(new[] { "mkt-1" }, markets);
    }

    [Fact]
    public async Task EvaluateEntryAsync_UsesExecutablePolicyWithoutCallingExternalSources()
    {
        var policy = Policy("mkt-1", DateTimeOffset.UtcNow.AddHours(1));
        var feed = new RecordingExecutableOpportunityPolicyFeed([policy]);
        var strategy = CreateStrategy(feed, entryCooldownSeconds: 0);
        await strategy.StartAsync();
        await strategy.SelectMarketsAsync();

        var signal = await strategy.EvaluateEntryAsync(CreateSnapshot(yesBid: 0.50m, yesAsk: 0.53m));

        Assert.Equal(1, feed.GetExecutableCalls);
        Assert.NotNull(signal);
        Assert.Equal(StrategySignalType.Entry, signal!.Type);
        var order = Assert.Single(signal.Orders);
        Assert.Equal(OutcomeSide.Yes, order.Outcome);
        Assert.Equal(OrderSide.Buy, order.Side);
        Assert.Equal(TimeInForce.Fok, order.TimeInForce);
        Assert.Equal(0.53m, order.Price);

        using var context = JsonDocument.Parse(signal.ContextJson!);
        Assert.Equal(policy.HypothesisId, context.RootElement.GetProperty("opportunityId").GetGuid());
        Assert.Equal(policy.PolicyId, context.RootElement.GetProperty("policyId").GetGuid());
        Assert.Equal(policy.ScoreId, context.RootElement.GetProperty("scoreId").GetGuid());
        Assert.Equal(policy.GateRunId, context.RootElement.GetProperty("gateRunId").GetGuid());
        Assert.Equal(policy.AllocationId, context.RootElement.GetProperty("allocationId").GetGuid());
        Assert.Equal(policy.ScoreVersion, context.RootElement.GetProperty("scoreVersion").GetString());
        Assert.Equal(policy.PolicyVersion, context.RootElement.GetProperty("policyVersion").GetString());
        Assert.Single(context.RootElement.GetProperty("evidenceIds").EnumerateArray());
    }

    [Fact]
    public async Task EvaluateEntryAsync_IgnoresStaleOrderBook()
    {
        var strategy = CreateStrategy([Policy("mkt-1", DateTimeOffset.UtcNow.AddHours(1))]);
        await strategy.StartAsync();
        await strategy.SelectMarketsAsync();

        var signal = await strategy.EvaluateEntryAsync(
            CreateSnapshot(yesBid: 0.50m, yesAsk: 0.53m, quoteAge: TimeSpan.FromMinutes(5)));

        Assert.Null(signal);
    }

    [Fact]
    public async Task EvaluateEntryAsync_IgnoresOverCycleBudgetPolicies()
    {
        var strategy = CreateStrategy(
            [Policy("mkt-1", DateTimeOffset.UtcNow.AddHours(1))],
            entryCooldownSeconds: 0,
            maxPerCycleNotional: 0.50m);
        await strategy.StartAsync();
        await strategy.SelectMarketsAsync();

        var signal = await strategy.EvaluateEntryAsync(CreateSnapshot(yesBid: 0.50m, yesAsk: 0.53m));

        Assert.Null(signal);
    }

    [Fact]
    public async Task EvaluateEntryAsync_IgnoresKillSwitch()
    {
        var strategy = CreateStrategy(
            [Policy("mkt-1", DateTimeOffset.UtcNow.AddHours(1))],
            entryCooldownSeconds: 0,
            riskManager: new TestRiskManager(killSwitchActive: true));
        await strategy.StartAsync();

        var markets = await strategy.SelectMarketsAsync();
        var signal = await strategy.EvaluateEntryAsync(CreateSnapshot(yesBid: 0.50m, yesAsk: 0.53m));

        Assert.Empty(markets);
        Assert.Null(signal);
    }

    [Fact]
    public async Task EvaluateExitAsync_TriggersOnTakeProfitFromExecutablePolicy()
    {
        var strategy = CreateStrategy([Policy("mkt-1", DateTimeOffset.UtcNow.AddHours(1))], exitCooldownSeconds: 0);
        await strategy.StartAsync();
        await strategy.SelectMarketsAsync();
        await ApplyFilledEntryAsync(strategy);

        var signal = await strategy.EvaluateExitAsync(CreateSnapshot(yesBid: 0.66m, yesAsk: 0.68m));

        Assert.NotNull(signal);
        Assert.Equal(StrategySignalType.Exit, signal!.Type);
        Assert.Contains("take_profit", signal.ContextJson!);
        var order = Assert.Single(signal.Orders);
        Assert.Equal(OrderSide.Sell, order.Side);
        Assert.Equal(0.66m, order.Price);
        Assert.Equal(2m, order.Quantity);
    }

    [Fact]
    public async Task EvaluateSignals_AreDeterministicForSameSnapshotAndPolicy()
    {
        var policy = Policy("mkt-1", DateTimeOffset.UtcNow.AddHours(1));
        var first = CreateStrategy([policy], entryCooldownSeconds: 0, exitCooldownSeconds: 0);
        var second = CreateStrategy([policy], entryCooldownSeconds: 0, exitCooldownSeconds: 0);
        await first.StartAsync();
        await second.StartAsync();
        await first.SelectMarketsAsync();
        await second.SelectMarketsAsync();
        var entrySnapshot = CreateSnapshot(yesBid: 0.50m, yesAsk: 0.53m);

        var firstEntry = await first.EvaluateEntryAsync(entrySnapshot);
        var secondEntry = await second.EvaluateEntryAsync(entrySnapshot);

        Assert.NotNull(firstEntry);
        Assert.NotNull(secondEntry);
        Assert.Equal(firstEntry!.ContextJson, secondEntry!.ContextJson);
        Assert.Equal(firstEntry.Orders, secondEntry.Orders);

        await ApplyFilledEntryAsync(first);
        await ApplyFilledEntryAsync(second);
        var exitSnapshot = CreateSnapshot(yesBid: 0.66m, yesAsk: 0.68m);

        var firstExit = await first.EvaluateExitAsync(exitSnapshot);
        var secondExit = await second.EvaluateExitAsync(exitSnapshot);

        Assert.NotNull(firstExit);
        Assert.NotNull(secondExit);
        Assert.Equal(firstExit!.ContextJson, secondExit!.ContextJson);
        Assert.Equal(firstExit.Orders, secondExit.Orders);
    }

    private static Task ApplyFilledEntryAsync(LlmOpportunityStrategy strategy)
        => strategy.OnOrderUpdateAsync(new StrategyOrderUpdate(
            "llm_opportunity",
            "entry-order-1",
            "mkt-1",
            "yes-token",
            OutcomeSide.Yes,
            OrderLeg.Single,
            StrategySignalType.Entry,
            OrderSide.Buy,
            OrderType.Limit,
            TimeInForce.Fok,
            0.53m,
            ExecutionStatus.Filled,
            2m,
            2m,
            DateTimeOffset.UtcNow));

    private static LlmOpportunityStrategy CreateStrategy(
        IReadOnlyList<ExecutableOpportunityPolicyDto> policies,
        int entryCooldownSeconds = 60,
        int exitCooldownSeconds = 15,
        decimal maxPerCycleNotional = 20m,
        int maxActiveOpportunities = 3,
        IRiskManager? riskManager = null)
        => CreateStrategy(
            new RecordingExecutableOpportunityPolicyFeed(policies),
            entryCooldownSeconds,
            exitCooldownSeconds,
            maxPerCycleNotional,
            maxActiveOpportunities,
            riskManager);

    private static LlmOpportunityStrategy CreateStrategy(
        IExecutableOpportunityPolicyFeed feed,
        int entryCooldownSeconds = 60,
        int exitCooldownSeconds = 15,
        decimal maxPerCycleNotional = 20m,
        int maxActiveOpportunities = 3,
        IRiskManager? riskManager = null)
    {
        return new LlmOpportunityStrategy(
            new StrategyContext
            {
                StrategyId = "llm_opportunity",
                ExecutionService = null!,
                OrderBookReader = null!,
                MarketCatalog = null!,
                RiskManager = riskManager ?? new TestRiskManager(),
                DecisionLogger = null!
            },
            feed,
            new StaticOptionsMonitor<LlmOpportunityOptions>(new LlmOpportunityOptions
            {
                Enabled = true,
                MaxMarkets = 20,
                MaxActiveOpportunities = maxActiveOpportunities,
                MaxPerCycleNotional = maxPerCycleNotional,
                EntryCooldownSeconds = entryCooldownSeconds,
                ExitCooldownSeconds = exitCooldownSeconds,
                MaxOrderBookAgeSeconds = 10,
                MaxSlippage = 0m
            }));
    }

    private static ExecutableOpportunityPolicyDto Policy(
        string marketId,
        DateTimeOffset validUntilUtc,
        ExecutableOpportunityPolicyStatus status = ExecutableOpportunityPolicyStatus.Active)
    {
        var now = DateTimeOffset.UtcNow;
        return new ExecutableOpportunityPolicyDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "policy-v1",
            "score-v1",
            marketId,
            OutcomeSide.Yes,
            status,
            0.62m,
            0.72m,
            0.05m,
            0.57m,
            0.65m,
            0.49m,
            0.04m,
            2m,
            10m,
            10m,
            2m,
            now.AddMinutes(-1),
            validUntilUtc,
            [Guid.NewGuid()]);
    }

    private static MarketSnapshot CreateSnapshot(
        decimal yesBid,
        decimal yesAsk,
        TimeSpan? quoteAge = null)
    {
        var now = DateTimeOffset.UtcNow;
        var quoteTime = now - (quoteAge ?? TimeSpan.Zero);
        var yesTop = new TopOfBookDto(
            "yes-token",
            new Price(yesBid),
            new Quantity(10m),
            new Price(yesAsk),
            new Quantity(10m),
            yesAsk - yesBid,
            quoteTime);
        var noTop = new TopOfBookDto(
            "no-token",
            new Price(1m - yesAsk),
            new Quantity(10m),
            new Price(1m - yesBid),
            new Quantity(10m),
            yesAsk - yesBid,
            quoteTime);

        return new MarketSnapshot(
            new MarketInfoDto
            {
                MarketId = "mkt-1",
                ConditionId = "condition-1",
                Name = "Test market",
                Status = "active",
                TokenIds = ["yes-token", "no-token"],
                ExpiresAtUtc = now.AddDays(1)
            },
            yesTop,
            noTop,
            now);
    }

    private sealed class RecordingExecutableOpportunityPolicyFeed : IExecutableOpportunityPolicyFeed
    {
        private readonly IReadOnlyList<ExecutableOpportunityPolicyDto> _policies;

        public RecordingExecutableOpportunityPolicyFeed(IReadOnlyList<ExecutableOpportunityPolicyDto> policies)
        {
            _policies = policies;
        }

        public int GetExecutableCalls { get; private set; }

        public Task<IReadOnlyList<ExecutableOpportunityPolicyDto>> GetExecutableAsync(
            int limit = 50,
            CancellationToken cancellationToken = default)
        {
            GetExecutableCalls++;
            return Task.FromResult<IReadOnlyList<ExecutableOpportunityPolicyDto>>(_policies.Take(limit).ToArray());
        }
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T value)
        {
            CurrentValue = value;
        }

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class TestRiskManager(bool killSwitchActive = false, bool strategyBlocked = false) : IRiskManager
    {
        public bool IsKillSwitchActive { get; } = killSwitchActive;

        public Task<RiskCheckResult> ValidateOrderAsync(RiskOrderRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(RiskCheckResult.Allow());

        public Task RecordOrderAcceptedAsync(RiskOrderRequest request, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordOrderUpdateAsync(RiskOrderUpdate update, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordOrderErrorAsync(string strategyId, string clientOrderId, string errorCode, string message,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ActivateKillSwitchAsync(string reason, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ActivateKillSwitchAsync(
            KillSwitchLevel level,
            string reasonCode,
            string reason,
            string? contextJson = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ActivateStrategyKillSwitchAsync(
            string strategyId,
            KillSwitchLevel level,
            string reasonCode,
            string reason,
            string? marketId = null,
            string? contextJson = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ResetKillSwitchAsync(string? strategyId = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public KillSwitchState GetKillSwitchState() => IsKillSwitchActive ? ActiveKillSwitch(null) : KillSwitchState.Inactive;

        public KillSwitchState GetStrategyKillSwitchState(string strategyId) =>
            strategyBlocked ? ActiveKillSwitch(strategyId) : KillSwitchState.Inactive;

        public bool IsStrategyBlocked(string strategyId) => strategyBlocked;

        public IReadOnlyList<KillSwitchState> GetAllActiveKillSwitches()
            => IsKillSwitchActive || strategyBlocked ? [ActiveKillSwitch(strategyBlocked ? "llm_opportunity" : null)] : [];

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
            => Task.CompletedTask;

        public Task ClearUnhedgedExposureAsync(string strategyId, string marketId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public RiskStateSnapshot GetStateSnapshot()
            => new(
                TotalOpenNotional: 0m,
                TotalOpenOrders: 0,
                TotalCapital: 0m,
                AvailableCapital: 0m,
                CapitalUtilizationPct: 0m,
                NotionalByStrategy: new Dictionary<string, decimal>(),
                NotionalByMarket: new Dictionary<string, decimal>(),
                OpenOrdersByStrategy: new Dictionary<string, int>(),
                UnhedgedExposures: []);

        private static KillSwitchState ActiveKillSwitch(string? strategyId)
            => new()
            {
                IsActive = true,
                Level = KillSwitchLevel.HardStop,
                Reason = "test",
                ReasonCode = "test",
                StrategyId = strategyId,
                ActivatedAtUtc = DateTimeOffset.UtcNow
            };
    }
}
