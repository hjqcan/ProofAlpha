using System.Text.Json;
using Autotrade.Domain.Abstractions.EventBus;
using Autotrade.Llm;
using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.OpportunityDiscovery.Application;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.OpportunityDiscovery.Application.Contract.Analysis;
using Autotrade.OpportunityDiscovery.Application.Evidence;
using Autotrade.OpportunityDiscovery.Domain.Entities;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using Autotrade.OpportunityDiscovery.Infra.Data.Context;
using Autotrade.OpportunityDiscovery.Infra.Data.Repositories;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetDevPack.Messaging;

namespace Autotrade.OpportunityDiscovery.Tests;

public sealed class OpportunityDiscoveryServiceTests
{
    [Fact]
    public async Task ScanApprovePublish_ProducesPublishedFeedWithTraceablePolicy()
    {
        await using var context = CreateContext();
        var repositories = CreateRepositories(context);
        var service = CreateService(
            repositories,
            new FakeMarketCatalogReader(Market()),
            [new StaticEvidenceSource()],
            new PromptDrivenLlmClient());
        var query = new OpportunityQueryService(repositories.OpportunityRepository, repositories.EvidenceRepository);

        var result = await service.ScanAsync(new OpportunityScanRequest("test", 0m, 0m, 5));

        var opportunity = Assert.Single(result.Opportunities);
        Assert.Equal(ResearchRunStatus.Succeeded, result.Run.Status);
        Assert.Equal(OpportunityStatus.Candidate, opportunity.Status);
        Assert.Equal(1, result.Run.EvidenceCount);
        Assert.Equal(1, result.Run.OpportunityCount);

        var explainService = new OpportunityEvidenceExplainService(repositories.EvidenceSnapshotRepository);
        var explanation = await explainService.ExplainAsync(opportunity.Id, opportunity.CreatedAtUtc.AddTicks(1));
        Assert.NotNull(explanation);
        Assert.False(explanation.CanPassLivePromotion);
        Assert.Single(explanation.Snapshot.Citations);
        Assert.Empty(explanation.Snapshot.OfficialConfirmations);
        Assert.Contains(
            explanation.BlockingReasons,
            reason => reason.Contains("non-official", StringComparison.OrdinalIgnoreCase));

        await service.ApproveAsync(new OpportunityReviewRequest(opportunity.Id, "test"));
        await service.PublishAsync(new OpportunityReviewRequest(opportunity.Id, "test"));

        var published = await query.GetPublishedAsync();
        var publishedOpportunity = Assert.Single(published);
        Assert.Equal(opportunity.Id, publishedOpportunity.OpportunityId);
        Assert.Equal(result.Run.Id, publishedOpportunity.ResearchRunId);
        Assert.Single(publishedOpportunity.EvidenceIds);
        Assert.Equal(opportunity.Id, publishedOpportunity.Policy.OpportunityId);
    }

    [Fact]
    public async Task ScanAsync_InvalidLlmDocumentCreatesNeedsReviewAndCannotPublish()
    {
        await using var context = CreateContext();
        var repositories = CreateRepositories(context);
        var service = CreateService(
            repositories,
            new FakeMarketCatalogReader(Market()),
            [new StaticEvidenceSource()],
            new LowConfidenceLlmClient());
        var query = new OpportunityQueryService(repositories.OpportunityRepository, repositories.EvidenceRepository);

        var result = await service.ScanAsync(new OpportunityScanRequest("test", 0m, 0m, 5));

        var opportunity = Assert.Single(result.Opportunities);
        Assert.Equal(OpportunityStatus.NeedsReview, opportunity.Status);
        Assert.Contains("confidence", opportunity.ScoreJson, StringComparison.OrdinalIgnoreCase);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveAsync(new OpportunityReviewRequest(opportunity.Id, "test")));
        Assert.Empty(await query.GetPublishedAsync());
    }

    [Fact]
    public async Task ScanAsync_DeduplicatedEvidenceStillUsesPersistedTraceIds()
    {
        await using var context = CreateContext();
        var repositories = CreateRepositories(context);
        var service = CreateService(
            repositories,
            new FakeMarketCatalogReader(
                Market("market-1", "Will candidate alpha win the 2026 election?"),
                Market("market-2", "Will candidate beta win the 2026 election?")),
            [new StaticEvidenceSource()],
            new PromptDrivenLlmClient());
        var query = new OpportunityQueryService(repositories.OpportunityRepository, repositories.EvidenceRepository);

        var result = await service.ScanAsync(new OpportunityScanRequest("test", 0m, 0m, 5));

        Assert.Equal(2, result.Opportunities.Count);
        Assert.Equal(1, result.Run.EvidenceCount);
        foreach (var opportunity in result.Opportunities)
        {
            var citedIds = JsonSerializer.Deserialize<List<Guid>>(opportunity.EvidenceIdsJson)
                ?? throw new InvalidOperationException("Opportunity evidence ids were not valid JSON.");
            var evidence = await query.GetEvidenceAsync(opportunity.Id);
            var citedEvidence = Assert.Single(evidence);
            Assert.Contains(citedEvidence.Id, citedIds);
        }
    }

    [Fact]
    public async Task PublishAsync_RejectsOpportunityThatWasNotApproved()
    {
        await using var context = CreateContext();
        var repositories = CreateRepositories(context);
        var opportunity = new MarketOpportunity(
            Guid.NewGuid(),
            "market-1",
            OpportunityOutcomeSide.Yes,
            0.62m,
            0.7m,
            0.05m,
            DateTimeOffset.UtcNow.AddHours(1),
            "reason",
            "[]",
            "{}",
            "{}",
            "{}",
            OpportunityStatus.Candidate,
            DateTimeOffset.UtcNow);
        await repositories.OpportunityRepository.AddRangeAsync([opportunity]);
        var service = CreateService(
            repositories,
            new FakeMarketCatalogReader(Market()),
            [new StaticEvidenceSource()],
            new PromptDrivenLlmClient());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PublishAsync(new OpportunityReviewRequest(opportunity.Id, "test")));
    }

    [Fact]
    public async Task EvidenceRepository_DeduplicatesWithinRunButAllowsDifferentRuns()
    {
        await using var context = CreateContext();
        var repository = new EvidenceItemRepository(context);
        var runId = Guid.NewGuid();
        var otherRunId = Guid.NewGuid();

        await repository.AddRangeDedupAsync([
            Evidence(runId, "same-hash", "https://example.com/1"),
            Evidence(runId, "same-hash", "https://example.com/1-duplicate")
        ]);
        await repository.AddRangeDedupAsync([
            Evidence(otherRunId, "same-hash", "https://example.com/1")
        ]);

        Assert.Single(await repository.GetByRunAsync(runId));
        Assert.Single(await repository.GetByRunAsync(otherRunId));
    }

    private static OpportunityDiscoveryService CreateService(
        RepositorySet repositories,
        IMarketCatalogReader marketCatalog,
        IReadOnlyList<IEvidenceSource> evidenceSources,
        ILlmJsonClient llmClient)
    {
        return new OpportunityDiscoveryService(
            marketCatalog,
            evidenceSources,
            llmClient,
            repositories.RunRepository,
            repositories.EvidenceRepository,
            repositories.SourceProfileRepository,
            repositories.EvidenceSnapshotRepository,
            repositories.OpportunityRepository,
            repositories.ReviewRepository,
            Options.Create(new OpportunityDiscoveryOptions
            {
                PaperOnly = true,
                MinEdge = 0.03m,
                MinConfidence = 0.55m,
                FreshEvidenceMaxAgeHours = 72,
                MaxEvidencePerMarket = 8,
                DefaultValidHours = 24,
                MaxMarketsPerScan = 20
            }),
            NullLogger<OpportunityDiscoveryService>.Instance);
    }

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

    private static RepositorySet CreateRepositories(OpportunityDiscoveryContext context)
    {
        return new RepositorySet(
            new ResearchRunRepository(context),
            new EvidenceItemRepository(context),
            new SourceProfileRepository(context),
            new SourceObservationRepository(context),
            new EvidenceSnapshotRepository(context),
            new MarketOpportunityRepository(context),
            new OpportunityReviewRepository(context));
    }

    private static MarketInfoDto Market(
        string marketId = "market-1",
        string name = "Will candidate alpha win the 2026 election?")
    {
        return new MarketInfoDto
        {
            MarketId = marketId,
            ConditionId = $"{marketId}-condition",
            Name = name,
            Category = "Politics",
            Slug = marketId,
            Status = "active",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(30),
            Volume24h = 1000m,
            Liquidity = 1000m,
            TokenIds = ["yes-token", "no-token"]
        };
    }

    private static EvidenceItem Evidence(Guid runId, string hash, string url)
    {
        return new EvidenceItem(
            runId,
            EvidenceSourceKind.Rss,
            "rss",
            url,
            "Candidate alpha momentum",
            "Fresh evidence",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            hash,
            "{}",
            0.65m);
    }

    private sealed record RepositorySet(
        ResearchRunRepository RunRepository,
        EvidenceItemRepository EvidenceRepository,
        SourceProfileRepository SourceProfileRepository,
        SourceObservationRepository SourceObservationRepository,
        EvidenceSnapshotRepository EvidenceSnapshotRepository,
        MarketOpportunityRepository OpportunityRepository,
        OpportunityReviewRepository ReviewRepository);

    private sealed class StaticEvidenceSource : IEvidenceSource
    {
        public string Name => "static";

        public Task<IReadOnlyList<NormalizedEvidence>> SearchAsync(
            EvidenceQuery query,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<NormalizedEvidence> evidence =
            [
                new NormalizedEvidence(
                    EvidenceSourceKind.Rss,
                    "rss",
                    "https://example.com/evidence",
                    "Candidate alpha gains support",
                    "Fresh article relevant to candidate alpha.",
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    "{}",
                    0.75m)
            ];
            return Task.FromResult(evidence);
        }
    }

    private sealed class PromptDrivenLlmClient : ILlmJsonClient
    {
        public Task<LlmJsonResult<T>> CompleteJsonAsync<T>(
            LlmJsonRequest request,
            Func<T, IReadOnlyList<string>>? validator = null,
            CancellationToken cancellationToken = default)
            where T : class
        {
            using var prompt = JsonDocument.Parse(request.UserPrompt);
            var root = prompt.RootElement;
            var marketId = root.GetProperty("market").GetProperty("marketId").GetString()
                ?? throw new InvalidOperationException("Prompt missing market id.");
            var evidenceId = root.GetProperty("evidence").EnumerateArray().First().GetProperty("id").GetGuid();
            var response = new OpportunityAnalysisResponse(
                [
                    new OpportunityAnalysisDocument
                    {
                        MarketId = marketId,
                        Outcome = OutcomeSide.Yes,
                        FairProbability = 0.62m,
                        Confidence = 0.72m,
                        Edge = 0.05m,
                        Reason = "Fresh evidence supports a paper-only candidate.",
                        EvidenceIds = [evidenceId],
                        EntryMaxPrice = 0.57m,
                        TakeProfitPrice = 0.65m,
                        StopLossPrice = 0.49m,
                        MaxSpread = 0.04m,
                        Quantity = 1m,
                        MaxNotional = 5m,
                        ValidUntilUtc = DateTimeOffset.UtcNow.AddHours(4)
                    }
                ],
                null);

            return Task.FromResult(new LlmJsonResult<T>((T)(object)response, "{}", "{}"));
        }
    }

    private sealed class LowConfidenceLlmClient : ILlmJsonClient
    {
        public Task<LlmJsonResult<T>> CompleteJsonAsync<T>(
            LlmJsonRequest request,
            Func<T, IReadOnlyList<string>>? validator = null,
            CancellationToken cancellationToken = default)
            where T : class
        {
            using var prompt = JsonDocument.Parse(request.UserPrompt);
            var root = prompt.RootElement;
            var marketId = root.GetProperty("market").GetProperty("marketId").GetString()
                ?? throw new InvalidOperationException("Prompt missing market id.");
            var evidenceId = root.GetProperty("evidence").EnumerateArray().First().GetProperty("id").GetGuid();
            var response = new OpportunityAnalysisResponse(
                [
                    new OpportunityAnalysisDocument
                    {
                        MarketId = marketId,
                        Outcome = OutcomeSide.Yes,
                        FairProbability = 0.62m,
                        Confidence = 0.20m,
                        Edge = 0.05m,
                        Reason = "Weak evidence should not pass the publish gate.",
                        EvidenceIds = [evidenceId],
                        EntryMaxPrice = 0.57m,
                        TakeProfitPrice = 0.65m,
                        StopLossPrice = 0.49m,
                        MaxSpread = 0.04m,
                        Quantity = 1m,
                        MaxNotional = 5m,
                        ValidUntilUtc = DateTimeOffset.UtcNow.AddHours(4)
                    }
                ],
                null);

            return Task.FromResult(new LlmJsonResult<T>((T)(object)response, "{}", "{}"));
        }
    }

    private sealed class FakeMarketCatalogReader : IMarketCatalogReader
    {
        private readonly IReadOnlyList<MarketInfoDto> _markets;

        public FakeMarketCatalogReader(params MarketInfoDto[] markets)
        {
            _markets = markets;
        }

        public MarketInfoDto? GetMarket(string marketId)
            => _markets.FirstOrDefault(m => string.Equals(m.MarketId, marketId, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<MarketInfoDto> GetAllMarkets() => _markets;

        public IReadOnlyList<MarketInfoDto> GetActiveMarkets() => _markets;

        public IReadOnlyList<MarketInfoDto> GetLiquidMarkets(decimal minVolume)
            => _markets.Where(m => m.Volume24h >= minVolume).ToList();

        public IReadOnlyList<MarketInfoDto> GetExpiringMarkets(TimeSpan within)
            => _markets.Where(m => m.ExpiresAtUtc is not null && m.ExpiresAtUtc <= DateTimeOffset.UtcNow.Add(within)).ToList();
    }

    private sealed class NoopDomainEventDispatcher : IDomainEventDispatcher
    {
        public void Dispatch(IEnumerable<DomainEvent> domainEvents) { }

        public Task DispatchAsync(IEnumerable<DomainEvent> domainEvents) => Task.CompletedTask;
    }
}
