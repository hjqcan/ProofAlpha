using Autotrade.MarketData.Application.Catalog;
using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.Repositories;
using Autotrade.MarketData.Domain.Shared.Enums;
using Autotrade.MarketData.Infra.BackgroundJobs.Jobs;
using Autotrade.Polymarket.Abstractions;
using Autotrade.Polymarket.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Autotrade.MarketData.Tests;

public sealed class MarketCatalogSyncJobTests
{
    [Fact]
    public async Task ExecuteAsync_WhenLaterGammaPageFails_KeepsFetchedPageInMemoryButDoesNotPersist()
    {
        var gammaClient = new FakeGammaClient(
            PolymarketApiResult<IReadOnlyList<GammaMarket>>.Success(200, new[]
            {
                CreateGammaMarket("market-1"),
                CreateGammaMarket("market-2")
            }),
            PolymarketApiResult<IReadOnlyList<GammaMarket>>.Failure(0, "timeout", null));
        var catalog = new MarketCatalog(NullLogger<MarketCatalog>.Instance);
        var repository = new RecordingMarketRepository();
        var job = CreateJob(gammaClient, catalog, repository, pageSize: 2, maxPages: 3);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ExecuteAsync());

        Assert.Equal(2, catalog.Count);
        Assert.Equal(new[] { 0, 2 }, gammaClient.Offsets);
        Assert.Empty(repository.Upserted);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFirstGammaPageFails_ThrowsForHangfireRetry()
    {
        var gammaClient = new FakeGammaClient(
            PolymarketApiResult<IReadOnlyList<GammaMarket>>.Failure(0, "timeout", null));
        var catalog = new MarketCatalog(NullLogger<MarketCatalog>.Instance);
        var repository = new RecordingMarketRepository();
        var job = CreateJob(gammaClient, catalog, repository, pageSize: 2, maxPages: 3);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ExecuteAsync());

        Assert.Equal(0, catalog.Count);
        Assert.Equal(new[] { 0 }, gammaClient.Offsets);
        Assert.Empty(repository.Upserted);
    }

    private static MarketCatalogSyncJob CreateJob(
        IPolymarketGammaClient gammaClient,
        IMarketCatalog catalog,
        IMarketRepository repository,
        int pageSize,
        int maxPages)
    {
        var services = new ServiceCollection()
            .AddSingleton(repository)
            .BuildServiceProvider();

        return new MarketCatalogSyncJob(
            gammaClient,
            catalog,
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new MarketCatalogSyncOptions
            {
                PageSize = pageSize,
                MaxPages = maxPages,
                IncludeClosed = false
            }),
            NullLogger<MarketCatalogSyncJob>.Instance);
    }

    private static GammaMarket CreateGammaMarket(string id) =>
        new()
        {
            Id = id,
            ConditionId = $"condition-{id}",
            Question = $"Question {id}",
            Active = true,
            Closed = false
        };

    private sealed class FakeGammaClient : IPolymarketGammaClient
    {
        private readonly Queue<PolymarketApiResult<IReadOnlyList<GammaMarket>>> _results;
        private readonly List<int> _offsets = new();

        public FakeGammaClient(params PolymarketApiResult<IReadOnlyList<GammaMarket>>[] results)
        {
            _results = new Queue<PolymarketApiResult<IReadOnlyList<GammaMarket>>>(results);
        }

        public IReadOnlyList<int> Offsets => _offsets;

        public Task<PolymarketApiResult<IReadOnlyList<GammaMarket>>> ListMarketsAsync(
            int limit = 100,
            int offset = 0,
            bool closed = false,
            string? order = "id",
            bool ascending = false,
            CancellationToken cancellationToken = default)
        {
            _offsets.Add(offset);
            var result = _results.Count > 0
                ? _results.Dequeue()
                : PolymarketApiResult<IReadOnlyList<GammaMarket>>.Success(200, Array.Empty<GammaMarket>());

            return Task.FromResult(result);
        }
    }

    private sealed class RecordingMarketRepository : IMarketRepository
    {
        public List<MarketDto> Upserted { get; } = new();

        public Task<MarketDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<MarketDto?>(null);

        public Task<MarketDto?> GetByMarketIdAsync(string marketId, CancellationToken cancellationToken = default) =>
            Task.FromResult<MarketDto?>(null);

        public Task<IReadOnlyList<MarketDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MarketDto>>(Array.Empty<MarketDto>());

        public Task<IReadOnlyList<MarketDto>> GetByStatusAsync(
            MarketStatus status,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MarketDto>>(Array.Empty<MarketDto>());

        public Task UpsertRangeAsync(IEnumerable<MarketDto> markets, CancellationToken cancellationToken = default)
        {
            Upserted.AddRange(markets);
            return Task.CompletedTask;
        }

        public Task AddAsync(MarketDto market, CancellationToken cancellationToken = default)
        {
            Upserted.Add(market);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(MarketDto market, CancellationToken cancellationToken = default)
        {
            Upserted.Add(market);
            return Task.CompletedTask;
        }

        public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Upserted.Count);
    }
}
