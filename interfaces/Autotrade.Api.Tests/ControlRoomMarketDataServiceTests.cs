using Autotrade.Api.ControlRoom;
using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.OrderBook;
using Autotrade.MarketData.Domain.Shared.ValueObjects;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Autotrade.Api.Tests;

public sealed class ControlRoomMarketDataServiceTests
{
    [Fact]
    public async Task GetOrderBookUsesLocalTopOfBookTimestampForFreshness()
    {
        var lastUpdatedUtc = DateTimeOffset.UtcNow.AddSeconds(-45);
        var orderBookReader = new FakeOrderBookReader(lastUpdatedUtc);
        var services = new ServiceCollection()
            .AddSingleton<IMarketCatalogReader>(new FakeMarketCatalogReader())
            .AddSingleton<IOrderBookReader>(orderBookReader)
            .BuildServiceProvider();

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new ControlRoomMarketDataService(
            services,
            cache,
            new TestOptionsMonitor<ControlRoomOptions>(new ControlRoomOptions
            {
                EnablePublicMarketData = false,
                OrderBookLevels = 10,
                OrderBookFreshSeconds = 5,
                OrderBookStaleSeconds = 30
            }),
            NullLogger<ControlRoomMarketDataService>.Instance);

        var orderBook = await service.GetOrderBookAsync(
            "market-1",
            "token-yes",
            "Yes",
            levels: 10,
            CancellationToken.None);

        Assert.NotNull(orderBook);
        Assert.Equal("LocalOrderBook", orderBook.Source);
        Assert.Equal(lastUpdatedUtc, orderBook.LastUpdatedUtc);
        Assert.Equal(ControlRoomOrderBookFreshness.Stale, orderBook.Freshness.Status);
        Assert.True(orderBook.Freshness.AgeSeconds >= 45);
        Assert.Equal(1, orderBookReader.TopOfBookCallCount);
    }

    private sealed class FakeMarketCatalogReader : IMarketCatalogReader
    {
        private readonly MarketInfoDto[] markets =
        [
            new()
            {
                MarketId = "market-1",
                ConditionId = "condition-1",
                Name = "Will the fixture market settle yes?",
                Category = "Politics",
                Slug = "fixture-market",
                Status = "Active",
                Volume24h = 3_500m,
                Liquidity = 25_000m,
                TokenIds = ["token-yes", "token-no"]
            }
        ];

        public MarketInfoDto? GetMarket(string marketId)
        {
            return markets.FirstOrDefault(market =>
                string.Equals(market.MarketId, marketId, StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyList<MarketInfoDto> GetAllMarkets()
        {
            return markets;
        }

        public IReadOnlyList<MarketInfoDto> GetActiveMarkets()
        {
            return markets;
        }

        public IReadOnlyList<MarketInfoDto> GetLiquidMarkets(decimal minVolume)
        {
            return markets.Where(market => market.Volume24h >= minVolume).ToArray();
        }

        public IReadOnlyList<MarketInfoDto> GetExpiringMarkets(TimeSpan within)
        {
            return markets;
        }
    }

    private sealed class FakeOrderBookReader(DateTimeOffset lastUpdatedUtc) : IOrderBookReader
    {
        public int TopOfBookCallCount { get; private set; }

        public TopOfBookDto? GetTopOfBook(string assetId)
        {
            TopOfBookCallCount++;
            return new TopOfBookDto(
                assetId,
                new Price(0.47m),
                new Quantity(42m),
                new Price(0.50m),
                new Quantity(38m),
                0.03m,
                lastUpdatedUtc);
        }

        public IReadOnlyList<PriceLevelDto> GetDepth(string assetId, int levels = 10)
        {
            return
            [
                new PriceLevelDto(0.47m, 42m, IsBid: true),
                new PriceLevelDto(0.46m, 25m, IsBid: true),
                new PriceLevelDto(0.50m, 38m, IsBid: false),
                new PriceLevelDto(0.51m, 30m, IsBid: false)
            ];
        }
    }
}
