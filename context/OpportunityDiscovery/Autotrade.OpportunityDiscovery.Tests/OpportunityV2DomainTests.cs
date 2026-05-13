using System.Text.Json;
using Autotrade.Domain.Abstractions.EventBus;
using Autotrade.OpportunityDiscovery.Application;
using Autotrade.OpportunityDiscovery.Domain.Entities;
using Autotrade.OpportunityDiscovery.Domain.Exceptions;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using Autotrade.OpportunityDiscovery.Infra.Data.Context;
using Autotrade.OpportunityDiscovery.Infra.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NetDevPack.Messaging;

namespace Autotrade.OpportunityDiscovery.Tests;

public sealed class OpportunityV2DomainTests
{
    [Fact]
    public void OpportunityHypothesis_InvalidLifecycleTransitionThrowsDomainException()
    {
        var hypothesis = Hypothesis();
        var ex = Assert.Throws<OpportunityLifecycleException>(() =>
            hypothesis.MarkBacktestPassed(
                "seed-1",
                [Gate(hypothesis.Id, OpportunityPromotionGateKind.Backtest)],
                "test",
                "backtest passed",
                [],
                DateTimeOffset.UtcNow));

        Assert.Equal("OpportunityLifecycle.InvalidTransition", ex.ErrorCode);
        Assert.Equal(OpportunityHypothesisStatus.Discovered, hypothesis.Status);
    }

    [Fact]
    public void OpportunityHypothesis_RecordsTransitionsAndRequiresLiveGateRecords()
    {
        var hypothesis = Hypothesis();
        var evidenceId = Guid.NewGuid();
        var scored = hypothesis.MarkScored(
            "score-v1",
            "scorer",
            "features scored",
            [evidenceId],
            DateTimeOffset.UtcNow);

        Assert.Equal(OpportunityHypothesisStatus.Discovered, scored.FromStatus);
        Assert.Equal(OpportunityHypothesisStatus.Scored, scored.ToStatus);
        Assert.Equal("scorer", scored.Actor);
        using (var evidenceIds = JsonDocument.Parse(scored.EvidenceIdsJson))
        {
            Assert.Equal(evidenceId, evidenceIds.RootElement.EnumerateArray().Single().GetGuid());
        }

        var backtestGate = Gate(hypothesis.Id, OpportunityPromotionGateKind.Backtest);
        hypothesis.MarkBacktestPassed("seed-1", [backtestGate], "backtester", "positive net pnl", [evidenceId], DateTimeOffset.UtcNow);
        var paperGate = Gate(hypothesis.Id, OpportunityPromotionGateKind.Paper);
        hypothesis.MarkPaperValidated([paperGate], "paper-runner", "paper fill quality ok", [evidenceId], DateTimeOffset.UtcNow);

        var missingGateError = Assert.Throws<OpportunityLifecycleException>(() =>
            hypothesis.MarkLiveEligible(
                [backtestGate, paperGate],
                Guid.NewGuid(),
                "gatekeeper",
                "missing risk and compliance gates",
                [evidenceId],
                DateTimeOffset.UtcNow));
        Assert.Equal("OpportunityLifecycle.MissingGate", missingGateError.ErrorCode);

        var policyId = Guid.NewGuid();
        var allGates = AllRequiredGates(hypothesis.Id);
        hypothesis.MarkLiveEligible(
            allGates,
            policyId,
            "gatekeeper",
            "all gates passed",
            [evidenceId],
            DateTimeOffset.UtcNow);

        var missingAllocationError = Assert.Throws<OpportunityLifecycleException>(() =>
            hypothesis.PublishLive(
                allGates,
                policyId,
                Guid.Empty,
                "publisher",
                "allocation missing",
                [evidenceId],
                DateTimeOffset.UtcNow));
        Assert.Equal("OpportunityLifecycle.MissingAllocation", missingAllocationError.ErrorCode);

        var published = hypothesis.PublishLive(
            allGates,
            policyId,
            Guid.NewGuid(),
            "publisher",
            "micro allocation armed",
            [evidenceId],
            DateTimeOffset.UtcNow);

        Assert.Equal(OpportunityHypothesisStatus.LivePublished, hypothesis.Status);
        Assert.Equal(OpportunityHypothesisStatus.LiveEligible, published.FromStatus);
        Assert.Equal(OpportunityHypothesisStatus.LivePublished, published.ToStatus);
    }

    [Fact]
    public async Task ExecutablePolicyFeed_ExcludesSuspendedExpiredAndFuturePolicies()
    {
        await using var context = CreateContext();
        var repository = new OpportunityV2Repository(context);
        var feed = new ExecutableOpportunityPolicyFeed(repository);
        var now = DateTimeOffset.UtcNow;
        var active = Policy(now.AddMinutes(-1), now.AddHours(1), edge: 0.08m);
        active.Activate(now);
        var suspended = Policy(now.AddMinutes(-1), now.AddHours(1), edge: 0.09m);
        suspended.Activate(now);
        suspended.Suspend(now);
        var expired = Policy(now.AddHours(-2), now.AddHours(-1), edge: 0.10m);
        var future = Policy(now.AddHours(1), now.AddHours(2), edge: 0.11m);
        future.Activate(now);

        await repository.AddExecutablePolicyAsync(active);
        await repository.AddExecutablePolicyAsync(suspended);
        await repository.AddExecutablePolicyAsync(expired);
        await repository.AddExecutablePolicyAsync(future);

        var executable = await feed.GetExecutableAsync();

        var policy = Assert.Single(executable);
        Assert.Equal(active.Id, policy.PolicyId);
        Assert.Equal(0.08m, policy.Edge);
    }

    private static OpportunityHypothesis Hypothesis()
        => new(
            Guid.NewGuid(),
            "market-1",
            OpportunityOutcomeSide.Yes,
            Guid.NewGuid(),
            "tape:market-1:2026-05-13T00:00:00Z",
            "prompt-v1",
            "model-v1",
            "Candidate hypothesis",
            DateTimeOffset.UtcNow);

    private static IReadOnlyList<OpportunityPromotionGate> AllRequiredGates(Guid hypothesisId)
        => OpportunityHypothesis.RequiredLiveGateKinds
            .Select(kind => Gate(hypothesisId, kind))
            .ToList();

    private static OpportunityPromotionGate Gate(
        Guid hypothesisId,
        OpportunityPromotionGateKind gateKind,
        OpportunityPromotionGateStatus status = OpportunityPromotionGateStatus.Passed)
        => new(
            hypothesisId,
            gateKind,
            status,
            "test",
            $"{gateKind} gate",
            "{}",
            "[]",
            DateTimeOffset.UtcNow);

    private static ExecutableOpportunityPolicy Policy(
        DateTimeOffset validFromUtc,
        DateTimeOffset validUntilUtc,
        decimal edge)
        => new(
            Guid.NewGuid(),
            "policy-v1",
            "market-1",
            OpportunityOutcomeSide.Yes,
            0.62m,
            0.72m,
            edge,
            0.57m,
            0.65m,
            0.49m,
            0.04m,
            1m,
            5m,
            validFromUtc,
            validUntilUtc,
            "[]",
            DateTimeOffset.UtcNow);

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

    private sealed class NoopDomainEventDispatcher : IDomainEventDispatcher
    {
        public void Dispatch(IEnumerable<DomainEvent> domainEvents) { }

        public Task DispatchAsync(IEnumerable<DomainEvent> domainEvents) => Task.CompletedTask;
    }
}
