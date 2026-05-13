using System.Text.Json;
using Autotrade.Domain.Abstractions.EventBus;
using Autotrade.OpportunityDiscovery.Application;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using Autotrade.OpportunityDiscovery.Infra.Data.Context;
using Autotrade.OpportunityDiscovery.Infra.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NetDevPack.Messaging;

namespace Autotrade.OpportunityDiscovery.Tests;

public sealed class OpportunityScoringServiceTests
{
    [Fact]
    public async Task ScoreAsync_PositiveNetEdgeAndCapacity_CanPromoteAndPersistsBreakdown()
    {
        await using var context = CreateContext();
        var service = CreateService(context);
        var request = Request(Features());

        var result = await service.ScoreAsync(request);

        Assert.True(result.CanPromote);
        Assert.True(result.Score.CanPromote);
        Assert.Empty(result.BlockingReasons);
        Assert.True(result.Score.NetEdge > 0m);
        Assert.NotEqual(result.Score.LlmFairProbability, result.Score.FairProbability);
        Assert.Equal(
            result.Score.FairProbability
            - result.Score.ExecutableEntryPrice
            - result.Score.FeeEstimate
            - result.Score.SlippageBuffer,
            result.Score.NetEdge);
        Assert.Contains("llmFairProbability is an input feature only", result.BreakdownJson, StringComparison.Ordinal);
        Assert.Contains("netEdge", result.BreakdownJson, StringComparison.Ordinal);

        var persistedSnapshot = await context.OpportunityFeatureSnapshots.SingleAsync();
        var persistedScore = await context.OpportunityScores.SingleAsync();
        Assert.Equal(result.FeatureSnapshot.Id, persistedSnapshot.Id);
        Assert.Equal(result.Score.Id, persistedScore.Id);
        Assert.Equal(persistedSnapshot.Id, persistedScore.FeatureSnapshotId);
        Assert.Equal(result.BreakdownJson, persistedScore.ComponentsJson);
    }

    [Fact]
    public async Task ScoreAsync_NonPositiveNetEdge_BlocksPromotion()
    {
        await using var context = CreateContext();
        var service = CreateService(context);
        var request = Request(Features(executableEntryPrice: 0.92m));

        var result = await service.ScoreAsync(request);

        Assert.False(result.CanPromote);
        Assert.False(result.Score.CanPromote);
        Assert.True(result.Score.NetEdge <= 0m);
        Assert.Contains(
            result.BlockingReasons,
            reason => reason.Contains("positive net edge required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScoreAsync_NoExecutableCapacity_BlocksPromotion()
    {
        await using var context = CreateContext();
        var service = CreateService(context);
        var request = Request(Features(executableCapacity: 0m));

        var result = await service.ScoreAsync(request);

        Assert.False(result.CanPromote);
        Assert.False(result.Score.CanPromote);
        Assert.True(result.Score.NetEdge > 0m);
        Assert.Contains(
            result.BlockingReasons,
            reason => reason.Contains("executable capacity required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScoreAsync_CalibrationBucketComparesScoredProbabilityAgainstMarketImpliedBaseline()
    {
        await using var context = CreateContext();
        var service = CreateService(context);
        var request = Request(Features(marketImpliedProbability: 0.54m));

        var result = await service.ScoreAsync(request);

        Assert.Equal(
            result.Score.FairProbability - result.Score.MarketImpliedProbability,
            result.Score.Edge);
        Assert.StartsWith("over-market:", result.Score.CalibrationBucket, StringComparison.Ordinal);
        using var breakdown = JsonDocument.Parse(result.BreakdownJson);
        Assert.Equal(
            "fairProbability - marketImpliedProbability",
            breakdown.RootElement.GetProperty("formula").GetProperty("edge").GetString());
    }

    private static OpportunityScoringRequest Request(OpportunityFeatureVector features)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "tape:market-1:2026-05-13T00:00:00Z",
            "features-v1",
            OpportunityScoringService.DefaultScoreVersion,
            features,
            new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero));

    private static OpportunityFeatureVector Features(
        decimal marketImpliedProbability = 0.55m,
        decimal executableEntryPrice = 0.55m,
        decimal executableCapacity = 20m)
        => new(
            OpportunityType.InformationAsymmetry,
            0.72m,
            0.95m,
            0.90m,
            500m,
            0.01m,
            0.005m,
            0.95m,
            0.02m,
            0.02m,
            0.95m,
            0.01m,
            marketImpliedProbability,
            executableEntryPrice,
            0.01m,
            0.02m,
            executableCapacity);

    private static OpportunityScoringService CreateService(OpportunityDiscoveryContext context)
        => new(new OpportunityV2Repository(context));

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
