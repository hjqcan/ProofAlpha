using Autotrade.Domain.Abstractions.EventBus;
using Autotrade.OpportunityDiscovery.Application;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.OpportunityDiscovery.Domain.Entities;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using Autotrade.OpportunityDiscovery.Infra.Data.Context;
using Autotrade.OpportunityDiscovery.Infra.Data.Repositories;
using Autotrade.Trading.Application.Contract.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetDevPack.Messaging;

namespace Autotrade.OpportunityDiscovery.Tests;

public sealed class OpportunityOperatorServiceTests
{
    [Fact]
    public async Task ExplainAsync_RedactsSensitiveJsonAndReturnsOperatorShape()
    {
        await using var context = CreateContext();
        var repository = new OpportunityV2Repository(context);
        var evidenceRepository = new EvidenceSnapshotRepository(context);
        var sourceRepository = new SourceProfileRepository(context);
        var now = DateTimeOffset.UtcNow;
        var hypothesis = Hypothesis();
        hypothesis.MarkScored("score-v1", "test", "scored", [], now);
        var snapshot = new EvidenceSnapshot(
            hypothesis.Id,
            hypothesis.ResearchRunId,
            hypothesis.MarketId,
            now,
            EvidenceSnapshotLiveGateStatus.Eligible,
            "[]",
            "{\"apiKey\":\"super-secret\",\"summary\":\"ok\"}",
            now);
        var citation = new EvidenceCitation(
            snapshot.Id,
            null,
            "official-source",
            EvidenceSourceKind.Manual,
            "Official Source",
            true,
            SourceAuthorityKind.Official,
            "https://example.test?api_key=super-secret",
            "official citation",
            now,
            now,
            "hash-1",
            1m,
            "{\"privateKey\":\"super-secret\",\"claim\":\"ok\"}",
            now);
        var score = new OpportunityScore(
            hypothesis.Id,
            Guid.NewGuid(),
            "score-v1",
            0.60m,
            0.62m,
            0.70m,
            0.07m,
            0.54m,
            0.55m,
            0.01m,
            0.01m,
            0.05m,
            5m,
            true,
            "bucket",
            "{\"secret\":\"super-secret\",\"netEdge\":0.05}",
            now);

        await repository.AddHypothesisAsync(hypothesis);
        await repository.AddScoreAsync(score);
        await evidenceRepository.AddAsync(new EvidenceSnapshotBundle(snapshot, [citation], [], []));
        await sourceRepository.AddAsync(new SourceProfile(
            "official-source",
            EvidenceSourceKind.Manual,
            "Official Source",
            SourceAuthorityKind.Official,
            true,
            0,
            "[\"manual\"]",
            0m,
            1m,
            1m,
            1,
            null,
            "initial",
            now));

        var service = CreateService(context, executablePolicies: []);

        var response = await service.ExplainAsync(hypothesis.Id);

        Assert.True(response.Redacted);
        Assert.NotNull(response.Hypothesis);
        Assert.NotNull(response.Evidence);
        Assert.DoesNotContain("super-secret", response.Evidence!.Snapshot.SummaryJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("super-secret", response.Evidence.Snapshot.Citations.Single().ClaimJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("super-secret", response.ScoreBreakdownJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[REDACTED]", response.ScoreBreakdownJson!);
        Assert.Single(response.SourceProfiles);
        Assert.Equal(0.25m, response.ExpectedEv);
    }

    [Fact]
    public async Task SuspendAsync_SuspendsOpportunityWithoutDeletingEvidence()
    {
        await using var context = CreateContext();
        var repository = new OpportunityV2Repository(context);
        var evidenceRepository = new EvidenceSnapshotRepository(context);
        var now = DateTimeOffset.UtcNow;
        var (hypothesis, policy, allocation) = await SeedLivePublishedAsync(repository, now);
        var snapshot = new EvidenceSnapshot(
            hypothesis.Id,
            hypothesis.ResearchRunId,
            hypothesis.MarketId,
            now,
            EvidenceSnapshotLiveGateStatus.Eligible,
            "[]",
            "{}",
            now);
        await evidenceRepository.AddAsync(new EvidenceSnapshotBundle(snapshot, [], [], []));
        var service = CreateService(context, executablePolicies: []);

        var result = await service.SuspendAsync(new OpportunityOperatorSuspendRequest(
            hypothesis.Id,
            "operator",
            "manual risk suspension"));

        Assert.True(result.Suspended);
        Assert.Equal(OpportunityHypothesisStatus.Suspended, (await context.OpportunityHypotheses.SingleAsync()).Status);
        Assert.Equal(ExecutableOpportunityPolicyStatus.Suspended, (await context.ExecutableOpportunityPolicies.SingleAsync()).Status);
        Assert.Equal(OpportunityLiveAllocationStatus.Suspended, (await context.OpportunityLiveAllocations.SingleAsync()).Status);
        Assert.Equal(hypothesis.Id, result.Transition!.HypothesisId);
        Assert.Equal(allocation.Id, result.Allocation!.Id);
        Assert.Equal(1, await context.EvidenceSnapshots.CountAsync());
    }

    private static OpportunityOperatorService CreateService(
        OpportunityDiscoveryContext context,
        IReadOnlyList<ExecutableOpportunityPolicyDto> executablePolicies)
    {
        var repository = new OpportunityV2Repository(context);
        return new OpportunityOperatorService(
            repository,
            new EvidenceSnapshotRepository(context),
            new SourceProfileRepository(context),
            new StubValidationGateService(),
            new OpportunityLiveAllocationService(
                repository,
                [],
                [],
                [],
                [],
                [],
                [],
                Options.Create(new OpportunityLiveAllocationOptions()),
                Options.Create(new ComplianceOptions())),
            new StaticExecutablePolicyFeed(executablePolicies),
            []);
    }

    private static async Task<(OpportunityHypothesis Hypothesis, ExecutableOpportunityPolicy Policy, OpportunityLiveAllocation Allocation)> SeedLivePublishedAsync(
        OpportunityV2Repository repository,
        DateTimeOffset now)
    {
        var hypothesis = Hypothesis();
        hypothesis.MarkScored("score-v1", "test", "scored", [], now);
        var backtestGate = Gate(hypothesis.Id, OpportunityPromotionGateKind.Backtest, now);
        hypothesis.MarkBacktestPassed("seed-1", [backtestGate], "test", "backtest", [], now.AddSeconds(1));
        var paperGate = Gate(hypothesis.Id, OpportunityPromotionGateKind.Paper, now.AddSeconds(2));
        hypothesis.MarkPaperValidated([paperGate], "test", "paper", [], now.AddSeconds(3));
        var policy = new ExecutableOpportunityPolicy(
            hypothesis.Id,
            "policy-v1",
            hypothesis.MarketId,
            OpportunityOutcomeSide.Yes,
            0.62m,
            0.70m,
            0.07m,
            0.55m,
            0.70m,
            0.45m,
            0.02m,
            5m,
            5m,
            now.AddMinutes(-1),
            now.AddHours(1),
            "[]",
            now);
        policy.Activate(now);
        var gates = OpportunityHypothesis.RequiredLiveGateKinds
            .Select((kind, index) => Gate(hypothesis.Id, kind, now.AddSeconds(10 + index)))
            .ToList();
        var allocation = new OpportunityLiveAllocation(
            hypothesis.Id,
            policy.Id,
            5m,
            5m,
            now.AddHours(1),
            "allocation",
            now);
        hypothesis.MarkLiveEligible(gates, policy.Id, "test", "eligible", [], now.AddSeconds(20));
        hypothesis.PublishLive(gates, policy.Id, allocation.Id, "test", "published", [], now.AddSeconds(21));

        await repository.AddHypothesisAsync(hypothesis);
        await repository.AddExecutablePolicyAsync(policy);
        await repository.AddLiveAllocationAsync(allocation);
        foreach (var gate in gates)
        {
            await repository.AddPromotionGateAsync(gate);
        }

        return (hypothesis, policy, allocation);
    }

    private static OpportunityHypothesis Hypothesis()
        => new(
            Guid.NewGuid(),
            "market-1",
            OpportunityOutcomeSide.Yes,
            Guid.NewGuid(),
            "tape:market-1",
            "prompt-v1",
            "model-v1",
            "test thesis",
            DateTimeOffset.UtcNow);

    private static OpportunityPromotionGate Gate(
        Guid hypothesisId,
        OpportunityPromotionGateKind kind,
        DateTimeOffset now)
        => new(
            hypothesisId,
            kind,
            OpportunityPromotionGateStatus.Passed,
            "test",
            $"{kind} passed",
            "{}",
            "[]",
            now);

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

    private sealed class StaticExecutablePolicyFeed(IReadOnlyList<ExecutableOpportunityPolicyDto> policies)
        : IExecutableOpportunityPolicyFeed
    {
        public Task<IReadOnlyList<ExecutableOpportunityPolicyDto>> GetExecutableAsync(
            int limit = 50,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ExecutableOpportunityPolicyDto>>(policies.Take(limit).ToList());
    }

    private sealed class StubValidationGateService : IOpportunityValidationGateService
    {
        public Task<OpportunityGateEvaluationResult> EvaluateBacktestAsync(
            OpportunityBacktestGateRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<OpportunityGateEvaluationResult> EvaluateShadowAsync(
            OpportunityShadowGateRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<OpportunityGateEvaluationResult> EvaluatePaperAsync(
            OpportunityPaperGateRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<OpportunityGateEvaluationResult> EvaluateOperationalGateAsync(
            OpportunityOperationalGateRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<OpportunityLiveEligibilityResult> TryMarkLiveEligibleAsync(
            OpportunityLiveEligibilityRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new OpportunityLiveEligibilityResult(false, null, null, [], ["not implemented"]));
    }

    private sealed class NoopDomainEventDispatcher : IDomainEventDispatcher
    {
        public void Dispatch(IEnumerable<DomainEvent> domainEvents) { }

        public Task DispatchAsync(IEnumerable<DomainEvent> domainEvents) => Task.CompletedTask;
    }
}
