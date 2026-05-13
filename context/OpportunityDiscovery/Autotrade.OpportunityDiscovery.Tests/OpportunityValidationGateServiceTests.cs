using Autotrade.Domain.Abstractions.EventBus;
using Autotrade.MarketData.Application.Contract.Tape;
using Autotrade.OpportunityDiscovery.Application;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.OpportunityDiscovery.Domain.Entities;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using Autotrade.OpportunityDiscovery.Infra.Data.Context;
using Autotrade.OpportunityDiscovery.Infra.Data.Repositories;
using Autotrade.Trading.Application.Contract.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NetDevPack.Messaging;

namespace Autotrade.OpportunityDiscovery.Tests;

public sealed class OpportunityValidationGateServiceTests
{
    [Fact]
    public async Task EvaluateBacktestAsync_PassingInputsPersistPassedGateAndStableReplaySeed()
    {
        await using var context = CreateContext();
        var runner = new FakeBacktestRunner();
        var service = CreateService(context, runner);
        var request = BacktestRequest();

        var first = await service.EvaluateBacktestAsync(request);
        var second = await service.EvaluateBacktestAsync(request);

        Assert.True(first.Passed);
        Assert.Equal(OpportunityPromotionGateStatus.Passed, first.Gate.Status);
        Assert.Equal(first.EvaluationRun.ReplaySeed, second.EvaluationRun.ReplaySeed);
        Assert.Contains("thresholds", first.MetricsJson, StringComparison.Ordinal);
        Assert.Equal(2, runner.CallCount);
        Assert.Equal(2, await context.OpportunityEvaluationRuns.CountAsync());
        Assert.Equal(2, await context.OpportunityPromotionGates.CountAsync());
    }

    [Fact]
    public async Task EvaluateBacktestAsync_RejectsFutureEvidenceWithoutCallingReplay()
    {
        await using var context = CreateContext();
        var runner = new FakeBacktestRunner();
        var service = CreateService(context, runner);
        var request = BacktestRequest(evidenceAsOfUtc: AsOf.AddMinutes(1));

        var result = await service.EvaluateBacktestAsync(request);

        Assert.False(result.Passed);
        Assert.Equal(0, runner.CallCount);
        Assert.Contains(
            result.BlockingReasons,
            reason => reason.Contains("future evidence", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(OpportunityEvaluationRunStatus.Succeeded, result.EvaluationRun.Status);
        Assert.Equal(OpportunityPromotionGateStatus.Failed, result.Gate.Status);
    }

    [Fact]
    public async Task EvaluatePaperAsync_UsesPaperValidationSourceAndBlocksNonPositivePnl()
    {
        await using var context = CreateContext();
        var paperSource = new FakePaperValidationSource(
            PaperSnapshot(netPnl: 0m, canConsiderLive: true, liveArmingUnchanged: true));
        var service = CreateService(context, paperSource: paperSource);
        var request = new OpportunityPaperGateRequest(
            Guid.NewGuid(),
            "paper-v1",
            "tape:paper",
            Guid.NewGuid(),
            GoodSampleMetrics(),
            PredictedEdge: 0.10m);

        var result = await service.EvaluatePaperAsync(request);

        Assert.True(paperSource.Called);
        Assert.False(result.Passed);
        Assert.Equal(OpportunityPromotionGateKind.Paper, result.Gate.GateKind);
        Assert.Contains(
            result.BlockingReasons,
            reason => reason.Contains("net paper PnL", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TryMarkLiveEligibleAsync_FailedRequiredGatePreventsTransition()
    {
        await using var context = CreateContext();
        var repository = new OpportunityV2Repository(context);
        var hypothesis = PaperValidatedHypothesis();
        await repository.AddHypothesisAsync(hypothesis);
        await AddRequiredGatesAsync(repository, hypothesis.Id, failedKind: OpportunityPromotionGateKind.Risk);
        var service = CreateService(context);

        var result = await service.TryMarkLiveEligibleAsync(new OpportunityLiveEligibilityRequest(
            hypothesis.Id,
            Guid.NewGuid(),
            "test",
            "attempt live eligibility",
            [Guid.NewGuid()]));

        Assert.False(result.LiveEligible);
        Assert.Contains(
            result.BlockingReasons,
            reason => reason.Contains("Risk gate is Failed", StringComparison.Ordinal));
        var persisted = await context.OpportunityHypotheses.SingleAsync(item => item.Id == hypothesis.Id);
        Assert.Equal(OpportunityHypothesisStatus.PaperValidated, persisted.Status);
        Assert.Empty(context.OpportunityLifecycleTransitions);
    }

    [Fact]
    public async Task TryMarkLiveEligibleAsync_AllLatestRequiredGatesPassedTransitionsHypothesis()
    {
        await using var context = CreateContext();
        var repository = new OpportunityV2Repository(context);
        var hypothesis = PaperValidatedHypothesis();
        await repository.AddHypothesisAsync(hypothesis);
        await AddRequiredGatesAsync(repository, hypothesis.Id);
        var service = CreateService(context);
        var policyId = Guid.NewGuid();
        var evidenceId = Guid.NewGuid();

        var result = await service.TryMarkLiveEligibleAsync(new OpportunityLiveEligibilityRequest(
            hypothesis.Id,
            policyId,
            "test",
            "all gates passed",
            [evidenceId]));

        Assert.True(result.LiveEligible);
        Assert.NotNull(result.Transition);
        Assert.Equal(OpportunityHypothesisStatus.LiveEligible, result.Hypothesis!.Status);
        Assert.Equal(policyId, result.Hypothesis.ActivePolicyId);
        var persisted = await context.OpportunityHypotheses.SingleAsync(item => item.Id == hypothesis.Id);
        Assert.Equal(OpportunityHypothesisStatus.LiveEligible, persisted.Status);
        Assert.Equal(policyId, persisted.ActivePolicyId);
        var transition = await context.OpportunityLifecycleTransitions.SingleAsync();
        Assert.Contains(evidenceId.ToString(), transition.EvidenceIdsJson, StringComparison.OrdinalIgnoreCase);
    }

    private static OpportunityBacktestGateRequest BacktestRequest(DateTimeOffset? evidenceAsOfUtc = null)
        => new(
            Guid.NewGuid(),
            "backtest-v1",
            "tape:market-1:2026-05-01/2026-05-12",
            evidenceAsOfUtc ?? AsOf.AddMinutes(-1),
            new MarketReplayBacktestRequest(
                "market-1",
                "token-1",
                EntryMaxPrice: 0.55m,
                TakeProfitPrice: 0.70m,
                StopLossPrice: 0.45m,
                Quantity: 10m,
                MaxNotional: 5.50m,
                FromUtc: AsOf.AddDays(-12),
                ToUtc: AsOf.AddDays(-1),
                AsOfUtc: AsOf),
            GoodSampleMetrics(),
            PredictedEdge: 0.10m,
            [Guid.NewGuid()]);

    private static OpportunityValidationSampleMetrics GoodSampleMetrics()
        => new(
            EvaluableSampleCount: 120,
            ObservationDays: 0,
            ScoredBrier: 0.12m,
            MarketImpliedBrier: 0.18m,
            SourceDriftScore: 0.02m,
            CalibrationDriftScore: 0.01m,
            CriticalRiskEventCount: 0);

    private static OpportunityPaperValidationSnapshot PaperSnapshot(
        decimal netPnl,
        bool canConsiderLive,
        bool liveArmingUnchanged)
        => new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            canConsiderLive ? "Passed" : "Failed",
            canConsiderLive,
            liveArmingUnchanged,
            DecisionCount: 150,
            TradeCount: 12,
            RiskEventCount: 0,
            CriticalRiskEventCount: 0,
            ObservationDays: 0,
            netPnl,
            AdverseSlippage: 0.01m,
            [Guid.NewGuid()],
            [new OpportunityPaperCriterionSnapshot("pnl", "PnL", "Passed", "test", [Guid.NewGuid()], [])],
            []);

    private static async Task AddRequiredGatesAsync(
        OpportunityV2Repository repository,
        Guid hypothesisId,
        OpportunityPromotionGateKind? failedKind = null)
    {
        foreach (var kind in OpportunityHypothesis.RequiredLiveGateKinds)
        {
            await repository.AddPromotionGateAsync(new OpportunityPromotionGate(
                hypothesisId,
                kind,
                kind == failedKind ? OpportunityPromotionGateStatus.Failed : OpportunityPromotionGateStatus.Passed,
                "test",
                $"{kind} gate",
                "{}",
                "[]",
                DateTimeOffset.UtcNow.AddMinutes((int)kind)));
        }
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

    private static OpportunityValidationGateService CreateService(
        OpportunityDiscoveryContext context,
        IMarketReplayBacktestRunner? runner = null,
        IOpportunityPaperValidationSource? paperSource = null)
        => new(
            new OpportunityV2Repository(context),
            runner ?? new FakeBacktestRunner(),
            paperSource is null ? [] : [paperSource],
            Array.Empty<ILiveArmingService>());

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

    private static DateTimeOffset AsOf { get; } = new(2026, 5, 13, 0, 0, 0, TimeSpan.Zero);

    private sealed class FakeBacktestRunner : IMarketReplayBacktestRunner
    {
        public int CallCount { get; private set; }

        public Task<MarketReplayBacktestResult> RunAsync(
            MarketReplayBacktestRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new MarketReplayBacktestResult(
                "runner-seed",
                "test-fill-model",
                Entered: true,
                Exited: true,
                new MarketReplayFill(request.FromUtc.AddMinutes(1), 0.50m, 10m, "entry"),
                new MarketReplayFill(request.ToUtc, 0.65m, 10m, "take_profit"),
                GrossPnl: 1.50m,
                EstimatedFees: 0.05m,
                NetPnl: 1.45m,
                []));
        }
    }

    private sealed class FakePaperValidationSource(OpportunityPaperValidationSnapshot snapshot)
        : IOpportunityPaperValidationSource
    {
        public bool Called { get; private set; }

        public Task<OpportunityPaperValidationSnapshot?> GetAsync(
            Guid sessionId,
            int limit = 1000,
            CancellationToken cancellationToken = default)
        {
            Called = true;
            return Task.FromResult<OpportunityPaperValidationSnapshot?>(snapshot with { SessionId = sessionId });
        }
    }

    private sealed class NoopDomainEventDispatcher : IDomainEventDispatcher
    {
        public void Dispatch(IEnumerable<DomainEvent> domainEvents) { }

        public Task DispatchAsync(IEnumerable<DomainEvent> domainEvents) => Task.CompletedTask;
    }
}
