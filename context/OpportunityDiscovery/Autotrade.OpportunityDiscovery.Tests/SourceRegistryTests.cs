using Autotrade.Domain.Abstractions.EventBus;
using Autotrade.OpportunityDiscovery.Application;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.OpportunityDiscovery.Application.Evidence;
using Autotrade.OpportunityDiscovery.Domain.Entities;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using Autotrade.OpportunityDiscovery.Infra.Data.Context;
using Autotrade.OpportunityDiscovery.Infra.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NetDevPack.Messaging;

namespace Autotrade.OpportunityDiscovery.Tests;

public sealed class SourceRegistryTests
{
    [Fact]
    public void SourcePackCatalog_IncludesRequiredOfficialAndLeadDiscoveryPacks()
    {
        var keys = SourcePackCatalog.Defaults
            .Select(item => item.SourceKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(SourceProfileKeys.PolymarketMarkets, keys);
        Assert.Contains(SourceProfileKeys.PolymarketOrderBook, keys);
        Assert.Contains(SourceProfileKeys.PolymarketTrades, keys);
        Assert.Contains(SourceProfileKeys.CryptoPriceOracle, keys);
        Assert.Contains(SourceProfileKeys.MacroOfficialData, keys);
        Assert.Contains(SourceProfileKeys.SecFilings, keys);
        Assert.Contains(SourceProfileKeys.WeatherAlerts, keys);
        Assert.Contains(SourceProfileKeys.SportsScheduleResults, keys);
        Assert.Contains(SourceProfileKeys.SportsInjuryReports, keys);
        Assert.Contains(SourceProfileKeys.ElectionOfficialResults, keys);
        Assert.Contains(SourceProfileKeys.ElectionPolling, keys);

        Assert.True(SourcePackCatalog.Resolve(EvidenceSourceKind.Polymarket, "markets").IsOfficial);
        Assert.False(SourcePackCatalog.Resolve(EvidenceSourceKind.Rss, "rss").IsOfficial);
        Assert.False(SourcePackCatalog.Resolve(EvidenceSourceKind.Gdelt, "gdelt").IsOfficial);
        Assert.False(SourcePackCatalog.Resolve(EvidenceSourceKind.OpenAiWebSearch, "openai").IsOfficial);
    }

    [Fact]
    public async Task SourceRegistryService_ProfileUpdatesAreVersioned()
    {
        await using var context = CreateContext();
        var repository = new SourceProfileRepository(context);
        var service = new SourceRegistryService(repository);

        await service.EnsureDefaultSourceProfilesAsync();
        var next = await service.AppendSourceProfileVersionAsync(new AppendSourceProfileVersionRequest(
            SourceProfileKeys.RssNews,
            EvidenceSourceKind.Rss,
            "rss-news",
            SourceAuthorityKind.News,
            false,
            600,
            ["news", "lead-discovery"],
            0.30m,
            0.20m,
            0.40m,
            "penalize conflicting election coverage"));

        Assert.Equal(2, next.Version);
        Assert.NotNull(next.SupersedesProfileId);

        var current = await service.ListCurrentProfilesAsync();
        var rss = Assert.Single(current, item => item.SourceKey == SourceProfileKeys.RssNews);
        Assert.Equal(2, rss.Version);
        Assert.Equal(0.40m, rss.ReliabilityScore);
        Assert.Equal(2, await context.SourceProfiles.CountAsync(item => item.SourceKey == SourceProfileKeys.RssNews));
    }

    [Fact]
    public async Task EvidenceSnapshotRepository_ReconstructsOpportunityEvidenceAsOfTime()
    {
        await using var context = CreateContext();
        var repository = new EvidenceSnapshotRepository(context);
        var opportunityId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var firstAt = new DateTimeOffset(2026, 5, 13, 1, 0, 0, TimeSpan.Zero);
        var secondAt = firstAt.AddMinutes(10);

        var first = CreateBundle(
            opportunityId,
            runId,
            firstAt,
            "first",
            [],
            []);
        var secondSnapshot = new EvidenceSnapshot(
            opportunityId,
            runId,
            "market-1",
            secondAt,
            EvidenceSnapshotLiveGateStatus.Blocked,
            "[\"Live promotion is blocked by unresolved high-severity source conflict.\"]",
            "{\"revision\":\"second\"}",
            secondAt);
        var secondCitation = OfficialCitation(secondSnapshot.Id, secondAt);
        var secondConflict = new EvidenceConflict(
            secondSnapshot.Id,
            "result-conflict",
            EvidenceConflictSeverity.High,
            "Official result and news lead disagree.",
            "[\"polymarket-markets\",\"rss-news\"]",
            true,
            secondAt);
        var secondConfirmation = new OfficialConfirmation(
            secondSnapshot.Id,
            SourceProfileKeys.PolymarketMarkets,
            EvidenceConfirmationKind.OfficialApi,
            "market-1:Yes",
            "https://polymarket.example.com/market-1",
            0.95m,
            secondAt,
            "{}");

        await repository.AddRangeAsync([
            first,
            new EvidenceSnapshotBundle(
                secondSnapshot,
                [secondCitation],
                [secondConflict],
                [secondConfirmation])
        ]);

        var firstRead = await repository.GetForOpportunityAsOfAsync(opportunityId, firstAt.AddSeconds(1));
        Assert.NotNull(firstRead);
        Assert.Contains("\"first\"", firstRead.Snapshot.SummaryJson, StringComparison.Ordinal);
        Assert.Empty(firstRead.Conflicts);

        var secondRead = await repository.GetForOpportunityAsOfAsync(opportunityId, secondAt.AddSeconds(1));
        Assert.NotNull(secondRead);
        var conflict = Assert.Single(secondRead.Conflicts);
        Assert.Equal("result-conflict", conflict.ConflictKey);
        Assert.True(conflict.BlocksLivePromotion);
    }

    [Fact]
    public async Task EvidenceExplainService_ShowsConflictsAndBlocksNewsOnlyLivePromotion()
    {
        await using var context = CreateContext();
        var repository = new EvidenceSnapshotRepository(context);
        var explain = new OpportunityEvidenceExplainService(repository);
        var opportunityId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var snapshot = new EvidenceSnapshot(
            opportunityId,
            runId,
            "market-1",
            now,
            EvidenceSnapshotLiveGateStatus.Blocked,
            "[\"Live promotion requires official API confirmation or strong multi-source confirmation; non-official news/search-only evidence is not sufficient.\"]",
            "{}",
            now);
        var citation = NewsCitation(snapshot.Id, now);
        var conflict = new EvidenceConflict(
            snapshot.Id,
            "headline-conflict",
            EvidenceConflictSeverity.Medium,
            "Two non-official news leads disagree on the event timing.",
            "[\"rss-news\",\"gdelt-doc\"]",
            false,
            now);

        await repository.AddAsync(new EvidenceSnapshotBundle(
            snapshot,
            [citation],
            [conflict],
            []));

        var result = await explain.ExplainAsync(opportunityId, now.AddSeconds(1));

        Assert.NotNull(result);
        Assert.False(result.CanPassLivePromotion);
        Assert.Single(result.Snapshot.Citations);
        Assert.Single(result.Snapshot.Conflicts);
        Assert.Contains(
            result.BlockingReasons,
            reason => reason.Contains("non-official", StringComparison.OrdinalIgnoreCase));
    }

    private static EvidenceSnapshotBundle CreateBundle(
        Guid opportunityId,
        Guid runId,
        DateTimeOffset asOf,
        string revision,
        IReadOnlyList<EvidenceConflict> conflicts,
        IReadOnlyList<OfficialConfirmation> confirmations)
    {
        var snapshot = new EvidenceSnapshot(
            opportunityId,
            runId,
            "market-1",
            asOf,
            EvidenceSnapshotLiveGateStatus.Blocked,
            "[\"Live promotion requires official API confirmation or strong multi-source confirmation; non-official news/search-only evidence is not sufficient.\"]",
            $"{{\"revision\":\"{revision}\"}}",
            asOf);

        return new EvidenceSnapshotBundle(snapshot, [NewsCitation(snapshot.Id, asOf)], conflicts, confirmations);
    }

    private static EvidenceCitation NewsCitation(Guid snapshotId, DateTimeOffset now)
        => new(
            snapshotId,
            Guid.NewGuid(),
            SourceProfileKeys.RssNews,
            EvidenceSourceKind.Rss,
            "rss-news",
            false,
            SourceAuthorityKind.News,
            "https://news.example.com/item",
            "News lead",
            now,
            now,
            Guid.NewGuid().ToString("N"),
            0.55m,
            "{}",
            now);

    private static EvidenceCitation OfficialCitation(Guid snapshotId, DateTimeOffset now)
        => new(
            snapshotId,
            Guid.NewGuid(),
            SourceProfileKeys.PolymarketMarkets,
            EvidenceSourceKind.Polymarket,
            "polymarket-clob-markets",
            true,
            SourceAuthorityKind.PrimaryExchange,
            "https://polymarket.example.com/market-1",
            "Polymarket market",
            now,
            now,
            Guid.NewGuid().ToString("N"),
            0.95m,
            "{}",
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

    private sealed class NoopDomainEventDispatcher : IDomainEventDispatcher
    {
        public void Dispatch(IEnumerable<DomainEvent> domainEvents) { }

        public Task DispatchAsync(IEnumerable<DomainEvent> domainEvents) => Task.CompletedTask;
    }
}
